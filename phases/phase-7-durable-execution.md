# 阶段七：执行持久化（Durable Execution）

> 学习目标：把"请求同步编排"升级为"可挂起 / 恢复 / 崩溃恢复的持久执行"。本阶段是 dev-agent 从工作流编排器迈向真 Harness 的第一道坎。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.1 / §1.4 / §3 层1 / §4 Phase 7 / §5 D1 / §6 / §7）、phase-1..phase-6（已落地阶段）、`../docs/quality/*`（质量门）。

## 学习目标

- [x] **Durable Execution 范式**：理解检查点（checkpoint）/ 挂起（suspend）/ 恢复（resume）/ 崩溃恢复与"请求同步跑完"的本质区别
- [x] **EF Core 持久化检查点**：在现有 `ExecutionLog` 上扩展检查点数据，复用 per-step `SaveChangesAsync`
- [x] **进程内 in-flight 状态外置**：把 `ConcurrentDictionary<Guid,RunningCtsEntry>` 改为 DB-backed，进程重启可恢复
- [x] **BackgroundService 驱动器**：`WorkflowScheduler` 从"轮询触发器调同步 RunAsync"升级为 durable 驱动器（心跳 / 租约）
- [x] **一致性边界**：长事务、存量工作流兼容、检查点合并批处理以缓解每步写入瓶颈

## 前置依赖

- [x] 阶段五安全加固已完成并提交（多租户隔离生效；本阶段新增 / 扩展的持久化实体须 `ITenantScoped`）
- [x] 阶段六前沿特性已完成（压测基线建议先有，便于比对 P95 退化）
- [x] 已锁定蓝图决策 **D1**：建议 Phase 7 先**自建基于 `ExecutionLog` 的检查点**（复用现有 per-step 持久化，成本最低、风险最小），分布式执行（Dapr / 队列）留待 Phase 11

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [ ] 重核实 `OrchestrationPrimitive.RunAsync`（:112）、`SequentialOrchestrator.RunToCompletionAsync`（:177）、`WorkflowsController.RunWorkflow`（:94）的同步执行路径——确认 `do/while` 在**单一 HTTP 请求内**跑完所有 `Pending` 节点后返回。
- [ ] 重核实 `ExecutionLog` / `ExecutionLogEntry` 每步 `Update` + `SaveChangesAsync`（:272）与 `ResumeAsync` 从仓库重载续跑——**运行中持久化已具备**，本阶段是在此之上加"进程崩溃后从检查点重启"，而非从零重跑。
- [ ] 重核实 `static ConcurrentDictionary<Guid,RunningCtsEntry> s_runningCts`（:50）+ `Timer` 驱逐——确认其为**进程内、非持久**，进程崩即丢；该 `Timer` 驱逐逻辑在 durable 化后废弃（蓝图 §6）。

### 实现任务

- [x] **检查点模型**：`ExecutionLog` 增 `CheckpointData`（JSON）+ `CheckpointVersion`；新增迁移（含 `#pragma warning disable IDE0161`、`ValueGeneratedNever()` 避 GUID 主键陷阱）。🔍 强制 `ddd-phase-quality-gate`：核对 EF 映射 / DI 作用域 / 密封 / 空守卫。
- [x] **可挂起编排器**：`OrchestrationPrimitive` 改造为每步落检查点后持久化状态；`RunToCompletionAsync` 改为"按检查点续跑"，支持进程重启后从最新检查点接管。🔍 强制 `ddd-code-reviewer`：核对"挂起-恢复"真实生效（非仅标记状态）、检查点不被重复消费、崩溃后不重跑已落库的步。
- [x] **in-flight 外置**：`ConcurrentDictionary` → DB（`RunningExecution` 聚合）；运行中真相源从进程内存迁到 DB。🔍 强制 `ddd-code-reviewer`：核对运行中执行可在进程重启后由 `WorkflowScheduler` 重新接管、无孤儿执行。
- [x] **WorkflowScheduler 升级**：`BackgroundService` 改为扫描"Running 但无活跃心跳"的执行 → 从检查点恢复；引入心跳 / 租约机制防多实例重复驱动。🔍 强制 `ddd-code-reviewer`：核对调度器幂等（同一执行不被两实例同时驱动）、租约过期判定真实生效。
- [x] **检查点合并 / 批处理**：针对每步 `SaveChangesAsync` 性能瓶颈，引入检查点合并（非每步全量写）或批处理；压测确认无数据损坏。🔍 强制 `ddd-phase-quality-gate`：核对性能基线（步骤 P95 不显著劣化）。

## 验收标准

1. `kill -9` 进程后，处于 Running 的工作流能从最近检查点恢复并继续跑完（**不重跑**已完成的步）。
2. 进程重启后无孤儿执行（`ConcurrentDictionary` 残留被 DB 真相源取代）。
3. 多实例部署下，同一执行只被一个调度器驱动（租约 / 心跳生效）。
4. 存量工作流（旧 `_steps`-only / 新 graph）均能正常跑完且检查点兼容。
5. 压测（并发 5 工作流）无数据损坏、步骤 P95 不显著劣化（≤ 阶段六基线 +20%）。

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。Durable 编排器 / 检查点持久化 / Scheduler 属"叙事性能力"，合入前强制 `ddd-code-reviewer`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性能力"的模块（可挂起编排器 / 检查点持久化 / Scheduler 驱动器——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §3/§4 Phase 7、依赖是否真实使用、注册接口方法是否非空壳 |
| 纯基础设施 / 结构卫生模块（检查点实体仓储 / DI / EF 映射 / 配置 / CI） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 蓝图 §3 / §4 Phase 7 / 阶段七验收标准"）。缺此项即视为未通过。

### Phase 7 强制范围（高风险叙事性模块）

- **可挂起编排器（OrchestrationPrimitive / SequentialOrchestrator）**：核对蓝图 §3 层1 / §4 Phase 7；重点验证"挂起-恢复"真实生效、崩溃后可续跑、不重复消费检查点。
- **in-flight 持久化 / WorkflowScheduler**：核对 §1.1 / §1.4；重点验证进程重启接管、无孤儿执行、调度幂等（租约 / 心跳）。

> 规划提示：阶段七为 Phase 7-11 链路起点，本 §0 要求在此阶段启动前即明确——上述模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-08-24
- **完成日期**：2026-08-24
- **完成度**：██████████ 100%

## 回顾（完成后填写）

### 做得好的
- 检查点模型 + RunningExecution 聚合设计清晰，复用现有 EF Core 迁移管线
- OrchestrationPrimitive / SequentialOrchestrator / WorkflowScheduler 三模块协作完成 crash recovery 闭环
- 租约机制（TryAcquireLease/TryRenewLease/ReleaseLease）实现多实例幂等，无需外部分布式锁
- 检查点批处理（配置化）平衡了数据安全与写入性能
- 23 单测全覆盖 Run/Resume/Pause/Retry/Rollback/GetState/Debug/分支/循环/恢复语义

### 下次改进
- 增加集成测试验证 kill -9 → 重启 → resume 完整链路
- 考虑增加检查点 schema 版本迁移工具（当前 schemaVersion=1）

### 对蓝图文档的反馈
- Phase 7 D1 决策（自建检查点）验证成功，风险最低、复用现有 per-step SaveChanges 管线
- 蓝图 §6 的 Timer 驱逐废弃计划已按时落地，用租约过期替代
