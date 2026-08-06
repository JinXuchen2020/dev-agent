# F25 · 工作流调试器（变量监视 + 单步重跑 + 错误分支）

> 状态：`done`。来源：F7 工作流平台化 program 子项 **⑦**。本文档为 feature-builder 取数单元。
> 2026-08-06 完成 §6 决策锁定（基于现有引擎实测：Pause/Resume/RetryStep/RollbackTo/GetState + RunNode + Blackboard 均已存在，F25 仅做 API 暴露 + 变量快照持久化 + 前端，零引擎侵入）；2026-08-06 全栈实跑闭环，feat/f25-workflow-debugger 分支，三道质量门全 PASS，前端 BDD E2E 覆盖核心路径。

## 0. 目标
为工作流提供「开发期可观测 + 可干预」的调试能力：实时变量监视、单节点运行/重跑（可带 override）、出错时从错误节点恢复、整体调试态查看。对标 Dify 调试模式 / LangGraph 断点。

## 1. 范围（v1，已按用户选型锁定）
**in**：
- **变量监视**：调试运行时展示各节点累积产出变量（复用 F20 `Blackboard`，由 `DebugSession` 跨节点持久化）。
- **引擎级单步全链**：`debug/run` 初始化会话 → `debug/step` 运行下一节点后暂停 → `debug/resume` 继续到完成。复用既有拓扑/分支/循环引擎。
- **单节点运行/重跑**：`debug/retry-node`（带可选 `overriddenConfig`）重跑指定节点，不重跑全链。
- **错误分支恢复**：节点失败后编辑配置（或传 override）后从该节点重跑。
- **状态/变量查看**：`GET debug/state` + `GET debug/variables`。
- **调试会话重置**：`POST debug/reset` 清 `DebugSession` + 复位节点状态。
- 多租户隔离（DebugSession 为 `ITenantScoped`）+ 审计（DebugRun / StepRetry）。

**out（v2，v1 不做）**：分布式断点持久化；调试会话共享（多用户）；与 F21 触发器联调。

## 2. 接口契约（后端，扩展 `WorkflowsController`，前缀 `api/v1/workflows/{id}`）
- `POST {id}/debug/run` body `{ initialContext? }` → 创建/重置 `DebugSession`（Initialized），返回 sessionId + 首节点预览。`[Authorize(Roles="Admin,Operator")]`
- `POST {id}/debug/step` body `{ sessionId }` → 运行下一 Pending 节点（拓扑序，重分支/循环），返回该节点结果 + 变量快照 + 会话状态（Paused/Completed/Failed）。`[Authorize(Roles="Admin,Operator")]`
- `POST {id}/debug/resume` body `{ sessionId }` → 从当前状态继续到完成，逐节点持久化 Blackboard 到会话。`[Authorize(Roles="Admin,Operator")]`
- `POST {id}/debug/retry-node` body `{ sessionId, nodeId, overriddenConfig? }` → 复位该节点为 Pending（可选覆盖 config）后运行该节点。`[Authorize(Roles="Admin,Operator")]`
- `POST {id}/debug/rollback` body `{ sessionId, targetStepOrder }` → `RollbackToAsync` 精确回滚。`[Authorize(Roles="Admin,Operator")]`
- `GET {id}/debug/state` → `WorkflowStateSnapshot`（节点状态/结果/错误）。`[Authorize]`
- `GET {id}/debug/variables` → `{ variables: Record<string,string> }`（解析 DebugSession.VariablesJson）。`[Authorize]`
- `POST {id}/debug/reset` body `{ sessionId? }` → 清会话 + 复位所有节点为 Pending。`[Authorize(Roles="Admin,Operator")]`

> 既有 `POST {id}/nodes/{nodeId}/run`（`RunNodeCommand`）保留为底层单节点原语（空 Blackboard，不接入会话），调试 UI 统一走上面的 session 端点。

## 3. 数据模型与改动面
- **新聚合 `DebugSession`**（`Domain/Aggregates/Debug/DebugSession.cs`，`ITenantScoped` + `IAggregateRoot`）：
  - `Id`(Guid), `WorkflowId`(Guid), `TenantId`(Guid, init), `Status`(DebugSessionStatus enum), `CurrentStepOrder`(int), `VariablesJson`(string, 默认 `"{}"`), `CreatedAt`/`UpdatedAt`。
  - 方法：`Initialize()`（复位到 Initialized，清 VariablesJson）、`RecordStep(int lastExecutedOrder, DebugSessionStatus status, IReadOnlyDictionary<string,string> vars)`（持久化累积变量）、`GetVariables()`（解析 VariablesJson）。会话创建/重置走 `StartOrResetAsync` 新建 `DebugSession`；`reset` 端点复用同一路径。
- **新枚举 `DebugSessionStatus`**：Initialized / Running / Paused / Completed / Failed / RolledBack。
- **EF**：`DebugSessions` 表（ToTable）+ `DebugSessionConfiguration`（ValueGeneratedNever on Id）。一次 `dotnet ef migrations add AddDebugSession`。
- **新仓储 `IDebugSessionRepository`** + Impl（EF，`DbSet<DebugSession>`）；DI 注册。
- **引擎重构（`SequentialOrchestrator`）**：抽取 `ExecuteNextPendingNodeAsync(workflow, blackboard, skip, loopBodyIds, executionOrder, ct)` 复用既有拓扑/分支/循环/重试逻辑；`RunSequentialAsync` 循环调用它（行为不变）；新增 `DebugStepAsync` / `DebugResumeAsync`（经 `IOrchestrationPrimitive` 暴露）复用它，Blackboard 由 DebugSession 装载/回写。
- **审计**：新增 `AuditActionType.DebugRun` / `StepRetry`（复用既有审计 handler）。
- 复用：`OrchestrationPrimitive.RollbackToAsync`、`WorkflowStateSnapshot`/`StepSnapshot`、F24 `ExecutionLog`（Trace 复用，不双写）。

## 4. 风险与缓解
- 🟠 偏高（用户 2026-08-06 确认选型 B：含引擎级单步全链 + 独立 DebugSession 表）：
  - 引擎重构：抽取 `ExecuteNextPendingNodeAsync` 复用既有循环体，行为保持等价；后端单测覆盖 debug/step/resume/retry/rollback + 变量累积。
  - 新聚合 + 迁移：单列 + 标准配置，一次迁移；`ITenantScoped` 复用既有 query filter。
- 缓解：调试能力不影响生产 `RunWorkflowCommand`；所有调试写操作限 Admin,Operator；多租户经 `ITenantScoped` 既有 filter 隔离。

## 5. 验收标准
- 调试会话初始化后，`debug/step` 逐节点运行，Blackboard 跨节点累积；`GET debug/variables` 返回正确变量字典。
- `debug/resume` 从当前状态跑到完成，结果与全量 `RunWorkflow` 一致；变量正确累积。
- `debug/retry-node`（带 override）仅重算该节点，下游不受影响；失败节点可恢复。
- `GET debug/state` 节点状态/结果/错误准确；`debug/reset` 干净复位。
- 多租户隔离；审计落库；前端 tsc 0 + qa.mjs 全绿 + BDD E2E 覆盖核心调试路径（初始化→单步→变量→重跑→重置）。

## 6. 决策（已锁定 — 2026-08-06 用户确认）
- **S1 引擎介入深度**：✅ **复用现有 orchestrator** + 抽取 `ExecuteNextPendingNodeAsync` 供 debug 复用。F25 不重写内核，仅增 debug 分支（最小侵入）。否决「独立调试执行器」。
- **S2 调试态存储**：✅ **独立 `DebugSession` 聚合 + 表**（用户选型 B）。变量与 Workflow 解耦，隔离更彻底；多租户经 `ITenantScoped`。
- **S3 重跑影响范围**：✅ 复用 `RetryStepAsync` / 会话内单节点重跑（重算该节点，下游经 resume 重算）。否决「仅重算该节点不更新下游」。
- **S4 与 F24 关系**：✅ 变量监视复用 `Blackboard` 字典；Trace 复用 `ExecutionLog`（F24 已落地）。F25 不双写。
- **S5 前端步进模型**：✅ **Approach B（引擎级单步全链，用户确认纳入 v1）**——`debug/run/step/resume` 由 orchestrator 驱动，复用拓扑/分支/循环。
- **S6 v1 范围**：✅ v1 = DebugSession + 变量监视（持久化 + GET）+ 引擎级单步(step/resume) + 单节点重跑(override) + 状态/变量查看 + 错误恢复(rollback/retry) + 会话重置。v2 增强 = 分布式断点 / 会话共享。
