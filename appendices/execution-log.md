## 附录 J：运行时日志管理

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：平台缺少一个统一的运行时日志体系来记录工作流执行的中间状态。现有 8.5 日志采集是基础设施层（Serilog），9.5 审计日志是安全合规层，两者都不能回答"这个工作流当前运行到哪一步了？""上一步出错了，输入是什么？"这类问题。本附录补上**执行日志层（ExecutionLog）**。

### J.1 执行日志 vs 其他日志的边界

```
┌────────────────────────────────────────────────────────────────┐
│  平台的三种日志体系                                              │
├────────────────────────────────────────────────────────────────┤
│                                                               │
│  系统日志 (Serilog)          ← 8.5  基础设施层                  │
│  - 模型调用超时、DB 连接失败、配置错误                           │
│  - 目标：运维排障                                               │
│  - 存储：Seq / Loki + 文件                                     │
│  - 保留：30 天                                                  │
│                                                               │
│  审计日志 (AuditLog)         ← 9.5  安全合规层                  │
│  - 谁、何时、做了什么操作（登录 / 改配置 / 调模型）              │
│  - 目标：安全追溯 + 合规                                        │
│  - 存储：PostgreSQL `AuditLog` 表（只追加）                     │
│  - 保留：永久（不可删除）                                       │
│                                                               │
│  执行日志 (ExecutionLog)     ← 本附录  业务追踪层               │
│  - 工作流每一步的输入/输出/耗时/错误/重试历史                    │
│  - 目标：用户可见的进度 + 调试 + 审计工作流本身                  │
│  - 存储：PostgreSQL `ExecutionLog` 表 + Redis 实时流            │
│  - 保留：90 天（详细 payload 30 天）                            │
│                                                               │
└────────────────────────────────────────────────────────────────┘
```

### J.2 ExecutionLog 表设计

```sql
-- Infrastructure/Persistence/Migrations/..._CreateExecutionLog.sql

CREATE TABLE execution_logs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL,
    workflow_id     UUID NOT NULL REFERENCES workflows(id),
    execution_id    UUID NOT NULL,                    -- 单次执行的唯一标识
    step_order      INT NOT NULL,                     -- 步骤序号（1-based）
    step_name       VARCHAR(128) NOT NULL,            -- 步骤名称（如"需求分析"）
    
    agent_id        UUID REFERENCES agents(id),       -- 执行该步骤的 Agent
    agent_type      VARCHAR(16) NOT NULL,              -- Agent 角色 Code（如 "REQ"）
    
    status          VARCHAR(20) NOT NULL DEFAULT 'pending',
                    -- pending | running | succeeded | failed | skipped | retrying
    
    input           JSONB,                            -- 步骤输入（截断 > 10KB）
    output          JSONB,                            -- 步骤输出（截断 > 10KB）
    error_detail    TEXT,                             -- 错误堆栈 / 错误消息
    
    duration_ms     INT,                              -- 步骤耗时（毫秒）
    retry_count     INT NOT NULL DEFAULT 0,            -- 已重试次数
    max_retries     INT NOT NULL DEFAULT 3,            -- 允许的最大重试次数
    
    model_used      VARCHAR(64),                      -- 实际使用的模型（含降级）
    token_usage     JSONB,                            -- { "prompt": 1200, "completion": 800 }
    
    parent_step_id  UUID REFERENCES execution_logs(id), -- 重试时指向原始步骤
    
    timeline        JSONB NOT NULL DEFAULT '[]',      -- 步骤内事件时间线
                    -- [{"ts": "...", "event": "started"},
                    --  {"ts": "...", "event": "llm_call_start", "model": "gpt-4o"},
                    --  {"ts": "...", "event": "llm_call_end", "duration_ms": 8234},
                    --  {"ts": "...", "event": "completed"}]
    
    is_fallback     BOOLEAN NOT NULL DEFAULT FALSE,   -- 是否由降级触发
    
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 索引
CREATE INDEX idx_execution_logs_workflow ON execution_logs(workflow_id, execution_id, step_order);
CREATE INDEX idx_execution_logs_status ON execution_logs(status) WHERE status IN ('running', 'failed');
CREATE INDEX idx_execution_logs_tenant ON execution_logs(tenant_id, created_at DESC);
```

**关键字段说明**：

| 字段 | 为什么这么设计 |
| :--- | :--- |
| `execution_id` | 同一工作流可以被执行多次，每次有独立的 execution_id |
| `parent_step_id` | 重试时新步骤指向原始步骤，形成重试链，方便追溯 |
| `timeline` | 步骤内部的微事件列表，而不是在多个表里做关联查询 |
| `input` / `output` JSONB 截断 | 防止超大模型输出撑爆数据库 |

### J.3 进度事件推送（SSE）

前端在发起工作流执行后，通过 SSE 接收实时进度更新：

```typescript
// 前端订阅方式
const eventSource = new EventSource(`/api/v1/workflows/${workflowId}/stream`);

eventSource.addEventListener("step_progress", (e) => {
  const data = JSON.parse(e.data);
  // {
  //   "executionId": "uuid",
  //   "stepOrder": 1,
  //   "totalSteps": 6,
  //   "stepName": "需求分析",
  //   "agentType": "REQ",
  //   "status": "running",          // running | completed | failed | retrying
  //   "progress": 0.42,             // 0.0 ~ 1.0（仅 running 时有意义）
  //   "message": "正在解析用户需求文档...",
  //   "durationMs": 8234,
  //   "retryCount": 0
  // }
});

eventSource.addEventListener("step_error", (e) => {
  const data = JSON.parse(e.data);
  // 错误详情，含错误码和降级信息
});

eventSource.addEventListener("workflow_completed", (e) => {
  const data = JSON.parse(e.data);
  // 工作流完成，含总耗时和步骤汇总
});
```

**后端推送实现**：

```csharp
// Application/Workflows/Services/ExecutionProgressService.cs
public class ExecutionProgressService
{
    private readonly ConcurrentDictionary<Guid, List<StreamWriter>> _subscribers = new();

    public async Task PushStepProgressAsync(Guid workflowId, StepProgressEvent evt)
    {
        if (!_subscribers.TryGetValue(workflowId, out var writers)) return;

        var payload = JsonSerializer.Serialize(evt);
        var deadWriters = new List<StreamWriter>();

        foreach (var writer in writers)
        {
            try
            {
                await writer.WriteAsync($"event: step_progress\ndata: {payload}\n\n");
                await writer.FlushAsync();
            }
            catch
            {
                deadWriters.Add(writer);
            }
        }

        // 清理断开的连接
        if (deadWriters.Count > 0)
            _subscribers[workflowId] = writers.Except(deadWriters).ToList();
    }
}
```

### J.4 日志查询 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/executions?workflow_id={id}&page=1&pageSize=20` | 工作流执行历史列表 | read:workflow |
| GET | `/api/v1/executions/{execution_id}/steps` | 单次执行的步骤列表 | read:workflow |
| GET | `/api/v1/executions/{execution_id}/steps/{step_order}` | 单步骤详情（含 input/output） | read:workflow |
| GET | `/api/v1/executions/errors?status=failed&from=...&to=...` | 按错误筛选执行步骤 | admin |

```json
// GET /api/v1/executions/{id}/steps 响应
{
  "data": [
    {
      "stepOrder": 1,
      "stepName": "需求分析",
      "agentType": "REQ",
      "status": "succeeded",
      "durationMs": 8234,
      "retryCount": 0,
      "modelUsed": "gpt-4o",
      "tokenUsage": { "prompt": 1200, "completion": 800 },
      "hasError": false,
      "createdAt": "2026-07-01T14:20:00Z"
    },
    {
      "stepOrder": 3,
      "stepName": "架构设计",
      "agentType": "ARC",
      "status": "failed",
      "durationMs": 96254,
      "retryCount": 3,
      "modelUsed": "deepseek",
      "tokenUsage": { "prompt": 4500, "completion": 3200 },
      "hasError": true,
      "errorSummary": "模型调用超时，所有重试后降级失败",
      "createdAt": "2026-07-01T14:35:00Z"
    }
  ]
}

// GET /api/v1/executions/{id}/steps/3 响应（含完整 payload）
{
  "data": {
    "stepOrder": 3,
    "stepName": "架构设计",
    "status": "failed",
    "input": {
      "userStory": "用户需要...",
      "requirements": ["功能A", "功能B"]
    },
    "output": null,
    "errorDetail": "SemanticKernelException: 模型 gpt-4o 连续 3 次超时\n  降级到 deepseek 后仍然超时\n  步骤已超过最大重试次数 (3)",
    "timeline": [
      { "ts": "2026-07-01T14:33:30Z", "event": "started" },
      { "ts": "2026-07-01T14:33:35Z", "event": "llm_call_start", "model": "gpt-4o" },
      { "ts": "2026-07-01T14:34:07Z", "event": "llm_call_end", "durationMs": 32100, "error": "timeout" },
      { "ts": "2026-07-01T14:34:07Z", "event": "retry", "retryCount": 1 },
      { "ts": "2026-07-01T14:34:40Z", "event": "llm_call_end", "durationMs": 31800, "error": "timeout" },
      { "ts": "2026-07-01T14:34:40Z", "event": "retry", "retryCount": 2 },
      { "ts": "2026-07-01T14:34:40Z", "event": "fallback", "from": "gpt-4o", "to": "deepseek" },
      { "ts": "2026-07-01T14:35:13Z", "event": "llm_call_end", "durationMs": 32354, "error": "timeout" },
      { "ts": "2026-07-01T14:35:13Z", "event": "failed", "reason": "max_retries_exceeded" }
    ],
    "durationMs": 96254,
    "retryCount": 3,
    "parentStepId": null
  }
}
```

### J.5 保留与清理策略

| 数据 | 保留期 | 清理方式 | 触发条件 |
| :--- | :--- | :--- | :--- |
| 执行日志（execution_logs 表行） | 90 天 | 定时任务 DELETE + VACUUM | 每日凌晨 3:00 |
| 详细 payload（input / output 字段） | 30 天 | UPDATE ... SET input = NULL, output = NULL | 同上的定时任务 |
| 实时 SSE 流 | 仅内存 | 连接断开即丢弃 | WebSocket / SSE 断连 |
| 失败的执行日志 | 永久（手动标记后才清理） | 标记 `archived` 后再做软删除 | 人工审核后 |

```csharp
// Infrastructure/BackgroundJobs/ExecutionLogCleanupJob.cs
public class ExecutionLogCleanupJob : IJob
{
    private readonly AppDbContext _db;

    public async Task Execute(IJobExecutionContext context)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var payloadCutoff = DateTime.UtcNow.AddDays(-30);

        // 1. 清理 90 天前的完整日志
        await _db.Database.ExecuteSqlAsync(
            $"DELETE FROM execution_logs WHERE created_at < {cutoff} AND status != 'failed'");

        // 2. 清理 30 天前的 payload（保留行但移除内容）
        await _db.Database.ExecuteSqlAsync(
            $"UPDATE execution_logs SET input = NULL, output = NULL WHERE created_at < {payloadCutoff}");

        // 3. VACUUM 回收空间（PostgreSQL 特有）
        await _db.Database.ExecuteSqlAsync("VACUUM execution_logs");
    }
}
```

### J.6 前端进度展示参考

```
┌──────────────────────────────────────────────────────────────────┐
│  正在执行：代码生成流水线                                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Step 1/6  需求分析师 · 解析用户需求         ✓  完成   8.2s      │
│    └─ 输入：用户需求文档（PDF）                                   │
│    └─ 输出：功能点列表（6 项）                                     │
│                                                                  │
│  Step 2/6  产品经理 · 定义用户故事           ✓  完成   5.1s      │
│                                                                  │
│  Step 3/6  架构师 · 设计技术方案             ⚠  重试 #2  32.1s   │
│    └─ 错误：模型 gpt-4o 超时 → 已降级到 deepseek                 │
│    └─ ████████████░░░░░░░░ 62%                                   │
│                                                                  │
│  Step 4/6  开发工程师 · 编写代码             ▓  运行中  42.3s    │
│    └─ ████████████████░░░░░░░░░░░ 55%                            │
│                                                                  │
│  Step 5/6  测试工程师 · 执行测试             ▒  排队中           │
│  Step 6/6  技术文档 · 编写文档               ▒  排队中           │
│                                                                  │
│  总耗时：1m 28s · 已使用 token：12,450 · 预计剩余：2m            │
│                                                                  │
│  [⏸ 暂停]  [✕ 取消]  [📋 查看详细日志]                          │
└──────────────────────────────────────────────────────────────────┘
```

### J.7 与附录 I（API 规范）的关系

执行日志 API 已经在附录 I 中预留了接口定义（I.6 对话 / I.7 监控），本附录补充了专用执行日志 API（J.4），建议合并到附录 I 中：

```diff
// I.3 工作流 API 补充
+ POST   /api/v1/workflows/{id}/execute     ← 已有
+ GET    /api/v1/workflows/{id}/stream       ← SSE 进度流（已有
+ GET    /api/v1/executions                  ← 执行历史列表（新增
+ GET    /api/v1/executions/{id}/steps       ← 步骤详情（新增
```

> **一句话总结**：`ExecutionLog` 表是平台第三种日志，聚焦"工作流每一步发生了什么"——通过 JSONB `timeline` 记录步骤内微事件、SSE 实时推送进度、90 天保留 + 30 天 payload 自动清理。三种日志（系统 / 审计 / 执行）各司其职，共同构成完整的可观测性体系。
