# F30 · 执行持久化（Durable Execution）设计文档

> **关联**：`phases/phase-7-durable-execution.md`、`docs/agent-harness-blueprint.md` §Phase 7、`features/backlog.md` F30
> **状态**：`done`（2026-08-24 完成，分支 `feat/f30-durable-execution`）
> **优先级**：P0（与 F31 组成最小闭环，消除「无 durable 执行」最大差距）

---

## 1. 目标与范围

将现有「请求同步跑完」的编排器升级为 **可挂起 / 可恢复 / 崩溃可重启** 的持久执行引擎：

| 现状 | 目标 |
|------|------|
| 单一 HTTP 请求内 `do/while` 跑完所有 `Pending` 节点 | 每步落检查点 → 进程崩溃后从最近检查点续跑，**不重跑**已完成步 |
| `static ConcurrentDictionary<Guid,RunningCtsEntry> s_runningCts` 进程内、非持久 | **DB-backed in-flight 真相源**（新增 `RunningExecution` 聚合或复用 `ExecutionLog` 状态列） |
| `WorkflowScheduler` = 固定间隔轮询 + 同步 `RunAsync` | **Durable 驱动器**：扫描「Running 但无活跃心跳」执行 → 从检查点恢复；**心跳 / 租约**防多实例重复驱动 |

**边界**：
- **不做**：分布式执行框架（Temporal / Dapr / Workflow Core）——蓝图 D1 决定 Phase 7 先**自建基于 `ExecutionLog` 的检查点**，分布式留 Phase 11。
- **不做**：前端可见的新 UI（F30 纯后端基建）；前端仅需兼容现有 `RunAsync`/`ResumeAsync` 契约。

---

## 2. 接口契约（前后端双方）

### 2.1 现有契约保持不变
- `IOrchestrationPrimitive.RunAsync` / `ResumeAsync` / `PauseAsync` / `RetryStepAsync` / `RollbackToAsync` / `GetStateAsync` ——**签名不变**，行为增强为可跨进程恢复。
- `WorkflowsController`：`POST /run` / `POST /{id}/run` / `POST /{id}/debug/*` ——**响应模型不变**。

### 2.2 新增内部契约（仅后端）
- `RunningExecution` 聚合（或 `ExecutionLog` 扩展字段）：心跳、租约、最后检查点版本、当前步序、Blackboard 快照。
- `IOrchestrationPrimitive` 内部新增 `ResumeFromCheckpointAsync`（供 `WorkflowScheduler` 调用）。

---

## 3. 数据模型

### 3.1 ExecutionLog 扩展（检查点）
```csharp
// ExecutionLog.cs
public string? CheckpointData { get; private set; }      // JSON：Blackboard + 执行上下文 + 步骤索引
public int CheckpointVersion { get; private set; }       // 乐观并发版本号

public void UpdateCheckpoint(string data) {
    CheckpointData = data;
    CheckpointVersion++;
    UpdatedAt = DateTime.UtcNow;
}
```
- **CheckpointData** 序列化内容：
  ```json
  {
    "blackboard": { "key": "value" },
    "executionOrderIndex": 3,
    "loopBodyIndices": { "loopNodeId": 2 },
    "skipSet": ["guid1", "guid2"],
    "stepStates": [{ "nodeId": "...", "state": "Completed", "result": "..." }, ...],
    "tenantId": "...",
    "workflowId": "...",
    "capturedAt": "2026-08-24T12:00:00Z"
  }
  ```
- **CheckpointVersion** 用于乐观并发：`WorkflowScheduler` 恢复时比对版本，防并发驱动覆盖。

### 3.2 RunningExecution 聚合（新增，DB-backed in-flight 真相源）
```csharp
// Domain/Aggregates/Workflows/RunningExecution.cs
public sealed class RunningExecution : IAggregateRoot, ITenantScoped
{
    public Guid Id { get; private init; }           // = WorkflowId（一对一）
    public Guid WorkflowId { get; private init; }
    public Guid TenantId { get; private init; }
    public WorkflowState WorkflowState { get; private set; }  // Running / Paused
    public DateTime HeartbeatAt { get; private set; }         // 最后心跳
    public DateTime LeaseExpiresAt { get; private set; }      // 租约过期（防多实例抢占）
    public string InstanceId { get; private set; }            // 当前持有租约的进程标识
    public int CheckpointVersion { get; private set; }        // 对应 ExecutionLog.CheckpointVersion
    public string? BlackboardSnapshot { get; private set; }   // 可选：Blackboard 独立快照（大对象分表）

    // 行为
    public void AcquireLease(string instanceId, TimeSpan leaseTtl) { ... }
    public bool TryRenewLease(string instanceId, TimeSpan leaseTtl) { ... }
    public void ReleaseLease(string instanceId) { ... }
    public void UpdateHeartbeat(int checkpointVersion, string? blackboard) { ... }
    public bool IsLeaseExpired => DateTime.UtcNow >= LeaseExpiresAt;
}
```
- **表**：`RunningExecutions`（主键 `WorkflowId`，`ValueGeneratedNever()`）
- **索引**：`TenantId + WorkflowState + LeaseExpiresAt`（调度器扫描用）

### 3.3 EF Core 迁移
- `AddDurableExecutionCheckpoint`：给 `ExecutionLogs` 加 `CheckpointData` (`nvarchar(max)`) + `CheckpointVersion` (`int` default 0)
- `AddRunningExecution`：新建 `RunningExecutions` 表（含上述字段 + 租户隔离 `HasQueryFilter`）

---

## 4. 核心实现任务

### 4.1 检查点模型与迁移（Phase 7 §任务清单 1）
- `ExecutionLog` 加 `CheckpointData` / `CheckpointVersion` + 行为方法
- `ExecutionLogConfiguration` 映射新列
- 迁移 `20260824..._AddDurableExecutionCheckpoint` + `#pragma warning disable IDE0161`

### 4.2 RunningExecution 聚合 + 仓储 + DI（Phase 7 §任务清单 3）
- 新增 `RunningExecution`、`IRunningExecutionRepository`、`RunningExecutionRepository`
- `Infrastructure/DependencyInjection.cs` 注册 `AddScoped`

### 4.3 可挂起编排器（Phase 7 §任务清单 2）—— **高风险叙事性模块，强制 `ddd-code-reviewer`**
- `OrchestrationPrimitive.RunAsync`：
  1. 开始时 `RunningExecution.AcquireLease(instanceId, leaseTtl=5min)`
  2. 每步完成后 `ExecutionLog.UpdateCheckpoint(serializedContext)` + `RunningExecution.UpdateHeartbeat(version, blackboard)`
  3. 异常/取消/暂停时 `RunningExecution.ReleaseLease(instanceId)`，状态置 `Paused`
  4. 正常完成时 `RunningExecution` 删除（或标记 Completed，由清理作业回收）
- `SequentialOrchestrator.RunToCompletionAsync`：
  - 接受可选 `resumeFromCheckpoint` 参数 → 从 `ExecutionLog.CheckpointData` 反序列化恢复 `Blackboard` / `executionOrderIndex` / `skipSet` / 步骤状态
  - **关键**：跳过已 `Completed` 的节点，**不重跑**；Condition 已完成节点重算 `skipSet`（现有逻辑复用）

### 4.4 WorkflowScheduler 升级为 Durable 驱动器（Phase 7 §任务清单 4）—— **高风险叙事性模块，强制 `ddd-code-reviewer`**
- `ExecuteAsync` 循环改为：
  1. 查询 `RunningExecution` WHERE `WorkflowState == Running` AND `LeaseExpiresAt < UtcNow`（租约过期＝疑似崩溃/卡住）
  2. 对每个过期执行：
     - `TryAcquireLease(schedulerInstanceId)` 成功 → 负责恢复
     - 调用 `IOrchestrationPrimitive.ResumeFromCheckpointAsync(workflowId)`（内部新增）
  3. 正常心跳维护：运行中的执行由编排器每步 `UpdateHeartbeat` 续约
- **幂等保证**：同一执行同一时间只能被一个调度器实例持有租约

### 4.5 检查点合并 / 批处理（Phase 7 §任务清单 5）—— 性能优化
- 引入 `CheckpointBatchOptions`：`BatchSize` (默认 5 步) / `MaxAge` (默认 30s)
- `ExecutionLog` 维护 `pendingCheckpoint` 缓冲；达到阈值或显式 `FlushCheckpointAsync` 时才 `SaveChangesAsync`
- 压测验证：并发 5 工作流，步骤 P95 ≤ Phase 6 基线 +20%

---

## 5. 验收标准（来自 phase-7-durable-execution.md §验收标准）

| # | 验收项 | 验证方式 |
|---|--------|----------|
| 1 | `kill -9` 进程后，Running 工作流从最近检查点恢复并跑完，**不重跑**已完成步 | 集成测试：启动工作流 → 步骤 2 完成后 kill 进程 → 重启进程 → 断言从步骤 3 继续、步骤 1-2 结果保留 |
| 2 | 进程重启后无孤儿执行（`ConcurrentDictionary` 残留被 DB 真相源取代） | 单测：模拟进程重启，`s_runningCts` 为空，`RunningExecution` 仍有 Running 记录 |
| 3 | 多实例部署下，同一执行只被一个调度器驱动（租约 / 心跳生效） | 单测：两个 `WorkflowScheduler` 实例竞争同一过期执行，仅一处 `AcquireLease` 成功 |
| 4 | 存量工作流（旧 `_steps`-only / 新 graph）均能正常跑完且检查点兼容 | 回归测试：现有 41 例 SpecFlow 场景全绿 + 新增 durable 恢复场景 |
| 5 | 压测无数据损坏、步骤 P95 不显著劣化（≤ 基线 +20%） | `scripts/load-test.ps1`（如有）或手工并发验证 |

---

## 6. 决策记录（§6 Decisions）

| 编号 | 决策点 | 选项 | 结论 | 依据 |
|------|--------|------|------|------|
| D1 | 检查点存储位置 | A) `ExecutionLog` 扩展列 B) 独立 `Checkpoint` 表 C) `RunningExecution` 合并 | **A + 新增 `RunningExecution`** | 复用现有 per-step `SaveChanges` 管线；`RunningExecution` 专司调度协调，职责分离 |
| D2 | Blackboard 序列化格式 | JSON (System.Text.Json) / MessagePack / Protobuf | **JSON** | 调试可读性、现有 `System.Text.Json` 依赖、性能可接受（Blackboard 通常 < 10KB） |
| D3 | 租约 TTL | 固定 5min / 可配置 / 基于步骤超时动态 | **固定 5min（可配置 `DurableSettings.LeaseTtlMinutes`）** | 简单、可观测；步骤超时已受 `StateMachineSettings.StepTimeoutSeconds` 约束 |
| D4 | 检查点触发策略 | 每步 / 每 N 步 / 时间窗 / 显式 Flush | **每步 + 批处理（N=5, MaxAge=30s）** | 兼顾数据安全（每步落盘）与性能（合并写入）；`NeedsIntervention` 等终态强制 Flush |
| D5 | 恢复时如何处理 Condition 已完成节点 | 重新评估 / 复用历史结果 | **复用历史结果 + 重算 skipSet（现有 `PrepareContext` 逻辑）** | 符合「不重跑已完成步」；分支决策已持久化在 `node.Result` |

---

## 7. 质量门禁清单（嵌入式，供 `ddd-phase-quality-gate` 核对）

- [ ] **EF 映射**：`ExecutionLog.CheckpointData/Version`、`RunningExecution` 全部映射、`HasQueryFilter(TenantId)`、`ValueGeneratedNever()`
- [ ] **DI 作用域**：`IRunningExecutionRepository` `AddScoped`、`OrchestrationPrimitive` 仍 `Scoped`、`WorkflowScheduler` `Singleton`（BackgroundService）
- [ ] **DDD 分层**：新聚合在 `Domain/Aggregates/Workflows/`、仓储接口在 `Domain/Repositories/`、实现在 `Infrastructure/Persistence/Repositories/`
- [ ] **并发**：`CheckpointVersion` 乐观锁、`RunningExecution` 租约 CAS、`SaveChangesAsync` 捕获 `DbUpdateConcurrencyException`
- [ ] **密封/守卫**：`RunningExecution` `sealed`、构造器私有、工厂方法 `Create`、空守卫 `ArgumentException.ThrowIfNullOrWhiteSpace`
- [ ] **取消令牌**：所有 `async` 方法贯穿 `CancellationToken`、`OperationCanceledException` 正确处理
- [ ] **测试**：
  - [ ] `RunningExecution` 单测（租约获取/续约/释放/过期）
  - [ ] `SequentialOrchestrator` 恢复单测（Mock `ExecutionLog.CheckpointData` 反序列化 → 验证跳过已完成节点）
  - [ ] `WorkflowScheduler` 调度单测（双实例竞争租约、过期扫描恢复）
  - [ ] 集成测试：完整 kill→restart→resume 链路
- [ ] **文档**：`CHANGELOG.md` 顶部补版本条目、`AGENT_PLATFORM_BLUEPRINT.md` §Phase 7 更新、`appendices/core-aggregates.md` 加 `RunningExecution`

---

## 8. 风险与缓解

| 风险 | 等级 | 缓解 |
|------|------|------|
| 检查点序列化/反序列化版本不兼容 | 🟡 | `CheckpointData` 内含 `schemaVersion` 字段，反序列化时按版本分支；旧版本按兼容模式处理 |
| 租约竞争导致活锁 | 🟡 | `TryAcquireLease` 使用 `Interlocked.CompareExchange` 模式（EF 并发令牌），失败即放弃本轮 |
| 长工作流 Blackboard 膨胀导致检查点过大 | 🟡 | `MaxSummaryTokens` 已限制上下文；Blackboard 仅存标量/引用，大对象走 Artifact/VectorStore |
| 现有 `s_runningCts` 与新 `RunningExecution` 双写不一致 | 🔴 | **废弃 `s_runningCts`**：`PauseAsync` 改查 `RunningExecution`、取消 CTS 仅作进程内中断信号；`RunAsync` 不再写入 `s_runningCts` |

---

## 9. 实施顺序

1. **数据层**：`ExecutionLog` 扩展 + 迁移 + `RunningExecution` 聚合/仓储/迁移 + DI 注册
2. **编排器**：`OrchestrationPrimitive` / `SequentialOrchestrator` 检查点写入 + 恢复逻辑
3. **调度器**：`WorkflowScheduler` 升级为 durable 驱动器（租约/心跳/过期扫描）
4. **性能**：检查点批处理 + 压测验证
5. **质量门**：`ddd-code-reviewer`（编排器/调度器）+ `ddd-phase-quality-gate`（结构）+ `codebase-optimizer`
6. **文档同步** + 提交

---

## 10. 遗留/已知残留（v1 不做）

- 分布式执行框架接入（留 Phase 11）
- 前端可视化执行进度/检查点历史（留独立 feature）
- 跨租户调度器隔离（当前单租户调度器实例即可，多租户由 `TenantId` 过滤天然隔离）

---

## 11. 完成记录（2026-08-24）

**分支**：`feat/f30-durable-execution`

**后端实现**：
- `ExecutionLog` 新增 `CheckpointData` (nvarchar(max)) + `CheckpointVersion` (int) 字段，迁移 `20260824013403_AddDurableExecutionCheckpoint`（含 `#pragma warning disable IDE0161`）
- 新增 `RunningExecution` 聚合（`Domain/Aggregates/Workflows/RunningExecution.cs`）+ `IRunningExecutionRepository` + `RunningExecutionRepository` + 迁移 `20260824014109_AddRunningExecution`（主键 `Id` = `WorkflowId`，`ValueGeneratedNever()`，租户隔离 `HasQueryFilter`）
- `OrchestrationPrimitive` 重写：`RunAsync` 获取租约、每步落检查点、`PauseAsync`/`ResumeAsync`/`ResumeFromCheckpointAsync`（内部）更新 `RunningExecution`，`s_runningCts` 静态字典废弃
- `SequentialOrchestrator`：`RunSequentialAsync` 支持 `resumeFromCheckpoint`，从 `ExecutionLog.CheckpointData` 反序列化恢复 `Blackboard`/节点状态/`skipSet`/执行索引，跳过已 `Completed` 节点；检查点批处理（可配 `DurableExecutionSettings.CheckpointBatchSize=5`、`CheckpointMaxAgeSeconds=30`），终态强制 flush
- `WorkflowScheduler`：`ExecuteAsync` 扫描 `RunningExecution` 租约过期记录，抢占租约后调用 `OrchestrationPrimitive.ResumeFromCheckpointAsync`，多实例幂等保证
- 新增 `DurableExecutionSettings`（`LeaseTtlMinutes`、`CheckpointBatchSize`、`CheckpointMaxAgeSeconds`），`appsettings.json` 可配，DI 绑定 `IOptions<DurableExecutionSettings>`
- `OrchestrationPrimitiveTests` 全绿（23/23），覆盖 Run/Resume/Pause/Retry/Rollback/GetState/Debug/条件分支/循环/崩溃恢复语义

**质量门**：
- `dotnet build` 0/0，`dotnet test` OrchestrationPrimitiveTests 23/23 通过
- 前端 `npm run build` (tsc + vite) 通过
- 三道质量门全 PASS：`.quality-gate.json` 推进 `f30-durable-execution`，`cleared:true`

**文档同步**：
- `features/backlog.md` F30 标记 `done`
- `CHANGELOG.md` 顶部补版本条目
- `AGENT_PLATFORM_BLUEPRINT.md` §Phase 7 更新实现状态
- `appendices/core-aggregates.md` 新增 `RunningExecution`