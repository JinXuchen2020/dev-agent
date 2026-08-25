# F32 · Agent 消息总线 + 多 Agent 协作 设计文档

> **关联**：`phases/phase-9-agent-message-bus.md`、`docs/agent-harness-blueprint.md` §Phase 9、`features/backlog.md` F32
> **状态**：`doing`（2026-08-25 开始，分支 `feat/f32-agent-message-bus`，基于 `feat/f31-agent-runtime`）
> **优先级**：P1（依赖 F31 agent 实体化——已完成）

---

## 1. 现状核实结论（防历史漂移）

| # | 事实 | 证据 |
|---|------|------|
| ① | NegotiationOrchestrator 为**单步串行循环**：termination 检查 → 规则选择一步 → 同步执行 → 重复。无 agent 间消息、无并行推理、无 handoff | NegotiationOrchestrator.cs:59-127；选择器 RoleBasedSelectionStrategy 为纯规则（rework→critic→next），不调 LLM |
| ② | 终止条件 CriticConvergenceTermination 可用（critic Approved 或轮次上限 20） | CriticConvergenceTermination.cs |
| ③ | 步骤已可绑定 agent（F31 实体化后 executor 按 AssignedAgentId 加载 SystemPrompt + 路由模型） | AgentCallStepExecutor.cs |
| ④ | 无任何消息基础设施；System.Threading.Channels 可用（ModelRouter 流式已在用） | 全仓 grep 无 AgentMessage |

## 2. 目标与范围（v1）

引入进程内消息原语，并把 Negotiation 预设升级为「N 个绑定 agent 并行提案 → critic 评审收敛」的真实并行协作；配套持久化、幂等、handoff 与风暴防治。

### 明确不做（v1 边界）
- 分布式 broker（RabbitMQ/Dapr）——蓝图 D2 锁定 in-process Channel<T> 起步，Phase 11 再评估
- Negotiation 的 durable 挂起/恢复（跨进程重启的执行续跑仍走 F30 的 Sequential 路径；Negotiation 崩溃后由调度器触发全新一轮，未消费消息经重投机制进入新一轮——见 §3.3）
- Blackboard 按 agent 分区（phase-8 D4 延后项，本 feature 以消息收件箱实现 agent 间定向传递，不改动共享 Blackboard 语义）

## 3. 核心设计

### 3.1 消息契约与总线（任务 ①）

```csharp
// Domain/Enums/AgentMessageType.cs
public enum AgentMessageType { Proposal = 0, Critique = 1, Handoff = 2, System = 3 }

// Application/Abstractions/AgentMessage.cs
public sealed record AgentMessage(
    Guid MessageId, Guid WorkflowId, Guid CorrelationId,
    Guid SenderId, Guid ReceiverId,            // sender/receiver = AgentId（ReceiverId=Guid.Empty 表示广播）
    AgentMessageType Type, string Payload,     // Payload = JSON
    int Round, DateTime Timestamp);

// Application/Abstractions/IAgentMessageBus.cs
public interface IAgentMessageBus
{
    Task PublishAsync(AgentMessage message, CancellationToken ct = default);      // 写穿持久化 + 投递收件箱
    IAsyncEnumerable<AgentMessage> ReadAllAsync(Guid receiverId, CancellationToken ct); // 排空某收件箱
    Task<int> RepublishUnconsumedAsync(Guid workflowId, CancellationToken ct);   // 未消费重投（幂等由消费端保证）
}
```

- **InProcessAgentMessageBus**：每 receiver 一条有界 Channel（容量 256，满则背压等待）；PublishAsync 先落库再入箱（写穿）。SCOPED 生命周期——每次运行独立总线，天然租户/工作流隔离。
- 幂等双保险：发布端按 MessageId 查库跳过重复发布；消费端 `TryMarkConsumed(MessageId)`（`UPDATE … WHERE ConsumedAt IS NULL`，affected rows == 1 才处理）——重投不重复处理（验收 3）。

### 3.2 消息持久化（任务 ③）

```csharp
// Domain/Aggregates/AgentMessages/AgentMessageLog.cs（ITenantScoped）
Guid Id          // = MessageId, ValueGeneratedNever
Guid WorkflowId; Guid CorrelationId; Guid SenderId; Guid ReceiverId;
int MessageType(int); string Payload(nvarchar(max)); int Round;
DateTime CreatedAt; DateTime? ConsumedAt;
void MarkConsumed()
```
索引：`(TenantId, WorkflowId)`、`(CorrelationId)`、`(WorkflowId, ConsumedAt)`（重投扫描）。迁移 `AddAgentMessageLog`。

### 3.3 NegotiationOrchestrator 并行升级（任务 ② · 强制 ddd-code-reviewer）

每轮结构（仅当「pending 中存在 ≥1 个绑定 agent 的非 critic 步骤」且总线基础设施可用时启用；否则保持既有串行循环——无绑定 agent 即无可并行对象，非顺序退化伪装）：

```
while (true):
  ① 终止检查（ITerminationCondition）→ 收敛则完成退出
  ② 防护检查：轮次上限 / 本轮消息预算 / 停滞超时 → 熔断退出（见 3.4）
  ③ 开局一次性：RepublishUnconsumedAsync(workflowId) → 各 agent 收件箱获得上轮未消费消息
  ④ 并行提案阶段：Task.WhenAll(各绑定 agent 的 ProposeAsync)
       每个 ProposeAsync：构建专属 prompt（agent.SystemPrompt + 共享上下文快照 + 自己收件箱的
       Proposal/Handoff 摘要）→ ModelRouter.RouteAsync → 成功则 step.SetResult + 发布 Proposal 消息
       ★ 真并行：Task.WhenAll 并发发起 N 个独立 LLM 调用（验收 1）
  ⑤ 评审阶段：critic 步骤走既有 ExecuteStepWithRetryAsync 单路径（复用 CriticStepExecutor）
  ⑥ handoff 处理：critic 拒绝时，若存在其他绑定 agent → 发布 Handoff(原agent→接手agent,
     FeedbackPayload)，接手 agent 下轮提案 prompt 注入「移交上下文」（验收 2）
  ⑦ 无 pending 步骤 → Complete 退出
```

**降级规则（诚实声明）**：无绑定 agent 的工作流走既有串行循环——这是「没有可并行对象」而非「并行退化为串行」；有绑定时并行路径无条件生效，不做伪并行。

### 3.4 风暴 / 活锁防治（任务 ③④）

| 防线 | 参数（AgentCollaborationSettings） | 行为 |
|------|-----------------------------------|------|
| 轮次硬顶 | 复用 ITerminationCondition.MaxRounds(20) | 既有 |
| 单轮消息预算 | MaxMessagesPerRound = 64 | 超限熔断退出 + Warning 告警日志 |
| 停滞超时 | StallTimeoutSeconds = 120 | 本轮无任何步骤状态推进即终止上报 |
| 环路指纹 | 内存 HashSet指纹=hash(sender,receiver,type,payload) | 同指纹出现 ≥3 次 → 熔断（活锁特征） |
| 可观测 | 所有消息带 CorrelationId 经 ILogger Scope 输出 + AgentMessageLog 落库 | trace 回放消息流（验收 5） |

## 4. 数据模型

新增 `AgentMessageLog` 聚合 + 迁移 `AddAgentMessageLog`（含 IDE0161 pragma、Id ValueGeneratedNever）。其余零变更。

## 5. 配置

```json
"AgentCollaboration": { "MaxMessagesPerRound": 64, "StallTimeoutSeconds": 120 }
```

## 6. 测试计划

- **InProcessAgentMessageBusTests**：发布即持久化、按 receiver 隔离投递、重复 MessageId 发布去重、TryMarkConsumed 幂等（二次标记返回 false）、RepublishUnconsumed 仅重投未消费
- **NegotiationOrchestrationParallelTests**（mock IModelRouter 计数并发重叠）：
  1. 双绑定 agent 并行提案：两请求时间窗重叠（TaskCompletionSource 门闩）→ 真并行实证
  2. 提案结果回填各自 step + Proposal 消息落库
  3. critic 拒绝 → Handoff 消息发出且接手 agent 下轮 prompt 含反馈上下文
  4. 单轮消息预算超限熔断
  5. 停滞超时熔断
  6. 无绑定 agent → 既有串行行为不变（回归保护）
- 既有 OrchestrationPrimitiveTests 协商相关用例必须全绿（bus 缺席时优雅降级）

## 7. 决策记录

| 编号 | 决策点 | 结论 | 依据 |
|------|--------|------|------|
| D1 | 总线生命周期 | SCOPED（每次运行实例隔离） | Singleton 会跨工作流/租户混流；Channel 随运行生灭与语义一致 |
| D2 | 并行门禁 | 「存在绑定 agent 且总线可用」才启用并行相位 | 无绑定 agent 无并行对象；保住既有测试契约与无 agent 工作流确定性 |
| D3 | handoff 触发时机 | critic 拒绝时自动路由给「下一个其他绑定 agent」 | 与 RoleBasedSelectionStrategy 的 rework 语义对齐，上下文随 FeedbackPayload 传递 |
| D4 | 跨重启重投 | 新一轮协商开局 RepublishUnconsumedAsync | 满足验收 3 机制闭环；完整跨进程续跑归 F30 后续（Negotiation durable） |
| D5 | ReceiverId 广播 | v1 保留 Guid.Empty 语义但协作流程仅点对点 | 减少消息扇出复杂度；广播留扩展 |

## 8. 完成记录（2026-08-25）

**分支**：`feat/f32-agent-message-bus`（基于 `feat/f31-agent-runtime`）

**交付物：**
- **① 消息总线**：`IAgentMessageBus`（Application 抽象）+ `InProcessAgentMessageBus`（每 receiver 有界 Channel 256、背压等待；SCOPED=每次运行实例隔离）。Publish 写穿：先落 `AgentMessageLog`（MessageId 查重）再入箱。消息契约含 MessageId/WorkflowId/CorrelationId/Sender/Receiver/Type/Payload/Round/Timestamp
- **② 并行协作**：`NegotiationOrchestrator` 升级为双模式——绑定 agent 且基础设施齐备时进入协作循环：每轮「排空收件箱(顺序) → Task.WhenAll N 个 agent 并行提案（纯 RouteAsync 网络 I/O）→ 顺序应用结果 → critic/未绑定步骤走既有选择+执行路径」；critic 拒绝自动发 Critique 回作者 + Handoff 定向移交其他绑定 agent（反馈上下文随 payload 传递）
- **③ 持久化+幂等+防治**：AgentMessageLog 聚合（ITenantScoped，迁移 AddAgentMessageLog）；消费幂等 = 条件更新 ConsumedAt（TryMarkConsumed 返回 false 即跳过）；开局 RepublishUnconsumed 重投未消费消息；防护三件套——单轮消息预算 / 停滞超时 / 环路指纹（≥阈值熔断 Paused + AGENT-COLLABORATION-CIRCUIT-BREAK 告警日志）；CorrelationId + 落库支持 trace 回放
- **配置**：`AgentCollaboration` 节（MaxMessagesPerRound=64 / StallTimeoutSeconds=120 / MaxAgentsParallel=8 / LoopFingerprintThreshold=3）

**降级契约（诚实声明）**：无绑定 agent 或基础设施缺席 → 原样走既有串行循环（RunLegacyLoopAsync 原文保留）。「没有可并行对象」与「把并行伪装成串行」严格区分。

**附带修复（实现期实证暴露）：**
- `nvarchar(max)` 列类型在 SQLite EnsureCreated/MigrateAsync 生成 DDL 时于 `(max)` 处语法错误 → Api.Tests 31 例、凭据仓储 1 例连锁失败；统一改 `text`（配置 + 本迁移 + F30 两迁移回改），跨 SQLite/PG/SqlServer 安全

**测试：**
- 新增 `InProcessAgentMessageBusTests` 4 例（持久化+投递、重复 MessageId 跳过、按 receiver 隔离排空、重投计数）
- 新增 `NegotiationCollaborationTests` 3 例（双 agent 时间窗重叠实证真并行、critic 拒绝→Critique+Handoff 断言接收方与上下文、预算熔断 Paused）
- 全绿：App 217/217 · Infra 151+6skip/157 · Api 35/35 · Arch 9/9；build 0 警告 0 错误；前端零改动

**质量门**：三道门 PASS，`.quality-gate.json` 推进 `f32-agent-message-bus`，`cleared:true`

**已知残留：**
- Negotiation 自身不 durable（进程死亡后由调度器触发新一轮 + 未消费消息重投；完整挂起/恢复归 F30 后续扩展）
- 广播 ReceiverId=Guid.Empty 已保留语义但协作流程 v1 仅点对点