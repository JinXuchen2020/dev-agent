# Feature: 工作流灵活编辑（Agentic 节点入画布 + 增量节点 API）

> 分支 `feat/workflow-flex-edit` · 2026-09-07 · 对应 dev-agent 编排平台

## 1. 背景与目标

用户希望像 Coze / Dify 一样灵活地可视化编辑工作流。经代码核实，平台**已有基于 React Flow 的可视化画布**
（`WorkflowCanvasPage` + `NodePalette` + `NodeConfigPanel` + `DagNode`），支持 15 种节点拖拽/连线/配置/撤销，
保存走整图 `PUT /workflows/{id}` → `Workflow.ReplaceGraph()`。

但存在两块明显短板（用户明确点名 #1 + #2）：
- **#1**：后端 `StepType.Agentic(15)` 已有 `AgenticStepExecutor`（ReAct 自驱循环），但**前端调色板未暴露**，画布上放不出。
- **#2**：领域层 `AddNode/AddEdge/RemoveNode/RemoveEdge` 已就绪，但**未暴露为独立增量端点**，编辑只能整图替换/import。

本 feature 补齐这两块，使编辑灵活度追平 Coze/Dify 的日常编排。

## 2. 范围

### #1 前端 Agentic 节点入画布
| 文件 | 改动 |
| :--- | :--- |
| `src/.../types/index.ts` | `StepType` const 增加 `Agentic: 15`；`NodeConfig` 增 `goal?: string` |
| `src/.../stores/workflowCanvasStore.ts` | `STEP_TYPE_TO_NODE_TYPE` / `NODE_TYPE_TO_STEP_TYPE` / `STEP_TYPE_LABEL` 补 `agentic`；`defaultConfig` 增 `Agentic` case |
| `src/.../components/canvas/NodePalette.tsx` | 调色板增加 Agentic 节点（图标 `NodeIndexOutlined`）+ 中英文案 |
| `src/.../components/canvas/DagNode.tsx` | `TYPE_ICON` 补 `Agentic`（否则 `Record<StepType,…>` 全键约束 tsc 报错） |
| `src/.../components/canvas/NodeConfigPanel.tsx` | `NODE_TYPE_LABEL` 补 `Agentic`；新增配置块：`agentId` Select（复用 Agents 列表）+ `goal` 文本域；Agents 列表加载条件扩展至 Agentic |
| `src/.../pages/WorkflowCanvasPage.tsx` | `nodeTypes` 映射补 `agentic: DagNode` |
| `src/.../locales/zh-CN.ts` & `en-US.ts` | `canvas.nodeType.agentic` / `canvas.nodeDesc.agentic` / `canvas.agenticGoal`(+tooltip) |

`AgenticStepExecutor` 契约（已核实）：从 `configJson` 读 `agentId`（或 `assignedAgentId`）与 `goal`（或节点名），
经 `AgenticOrchestrator.RunGoalAsync` 跑 ReAct 循环。故配置面板暴露 `agentId` + `goal` 即可。

### #2 后端增量节点/连线 API
领域方法签名改造（仅测试与新建 handler 调用，安全）：
- `Workflow.AddNode(...)` 由 `void` 改为返回 `WorkflowNode`（供 handler 回传服务端生成的 Guid）
- `Workflow.AddEdge(...)` 由 `void` 改为返回 `WorkflowEdge`

新增 4 个 MediatR Command（沿用 `UpdateWorkflow` 的 Request/Command 分层约定）：

| 端点 | Command | 请求体 | 响应 |
| :--- | :--- | :--- | :--- |
| `POST /workflows/{id}/nodes` | `AddWorkflowNodeCommand` | `{type,name,positionX,positionY,config,assignedAgentId}` | `WorkflowNodeResponse`（含服务端 id） |
| `DELETE /workflows/{id}/nodes/{nodeId}` | `RemoveWorkflowNodeCommand` | — | `204` / `404` |
| `POST /workflows/{id}/edges` | `AddWorkflowEdgeCommand` | `{sourceNodeId,targetNodeId,label}` | `WorkflowEdgeResponse`（含服务端 id） |
| `DELETE /workflows/{id}/edges/{edgeId}` | `RemoveWorkflowEdgeCommand` | — | `204` / `404` |

Handler 统一约定（与 `UpdateWorkflowCommandHandler` 一致）：
1. `repo.GetByIdAsync` → 不存在或租户不符 → 返回 null → 控制器 `404`（不泄露存在性）
2. `wf.CurrentState` 为 `Running/Paused` → 抛 `WorkflowConflictException`（→ `409`，已有处理器）
3. 调用领域方法（领域校验失败抛 `WorkflowGraphException` → `422`，已有处理器）
4. `_repo.Update(wf)` → `UnitOfWorkBehavior` 提交

复用 `GetWorkflowQuery.WorkflowNodeResponse` / `WorkflowEdgeResponse` 作为返回类型，避免新响应类型。

## 3. 前端保存链路决策（关键）

**本轮 #2 仅暴露后端增量端点，画布仍走既有整图 `updateWorkflow` 保存。**
理由：store 以客户端 UUID 为 React Flow id 真相源，`toPayload()` 整图发送；若同时跑增量端点 + 整图保存，
`ReplaceGraph` 按 id 重映射会产生重复节点。增量端点作为**程序化/后续画布改造的能力底座**，
真实浏览器 E2E 不依赖它们（避免引入不稳定双写）。后续可单独排期把画布单点增删切到增量端点。

同时新增 `api.ts` 中 `addWorkflowNode / removeWorkflowNode / addWorkflowEdge / removeWorkflowEdge` 类型化客户端函数，
使端点对前端消费者可用。

## 4. RBAC
沿用 `UpdateWorkflow`：`[Authorize(Roles = "Admin,Operator")]`。

## 5. 测试与质量门
- 后端：新增 4 个 handler 单元测试（租户不符 404、运行态 409、非法图 422、成功返回服务端 id），目录 `AgentPlatform.Application.Tests/Handlers/Workflows`。
- 前端：`tsc --noEmit` 0 error；`bddgen` exit 0。
- 真实浏览器 E2E：本 feature 不引入新 E2E 场景（增量端点留待后续画布集成时补）。
- 质量门：聚焦式修复报告 `docs/quality/workflow-flex-edit-gate.md`，`.quality-gate.json` 同笔暂存。

## 6. 校验清单（嵌入本文件，质量门依据）
- [x] 后端 build 0/0（全解决方案）
- [x] 4 个增量 handler 单测通过（租户/运行态/图校验/成功）+ 1 项 `ICommand` 回归锁
- [x] 前端 `tsc` 0 error、`bddgen` exit 0、`vite build` 通过
- [x] `StepType.Agentic=15` 前后端一致（后端 `StepType.cs:55`）
- [x] `AgenticStepExecutor` 契约（`agentId`+`goal`）与前端配置面板字段对齐
- [x] 既有 `UpdateWorkflow` 整图保存不受影响（回归：App 296 全绿）
- [x] 无新增死码 / 未使用 import
- [x] **真实联调（curl + cookie 登录）**：加节点→详情可见→加边→删边 204→删节点 204

## 7. 实现期发现：P0「假成功」——命令未实现 `ICommand<T>` 导致变更不落库

初版 4 个命令声明为 `IRequest<T>`。而 `UnitOfWorkBehavior` 的泛型约束是
`where TRequest : ICommand<TResponse>`（`Application/Abstractions/ICommand.cs`），
因此**不触发 `SaveChanges`**：端点返回 200 + 服务端生成的 Id，但数据库毫无变化——
本地 curl 联调（`POST /nodes` 后重新 `GET /workflows/{id}` 看不到新节点）实证。

- 修复：4 个命令改 `ICommand<WorkflowNodeResponse?> / ICommand<bool> / ICommand<WorkflowEdgeResponse?> / ICommand<bool>`。
- 回归锁：`WorkflowNodeEdgeHandlersTests.IncrementalGraphCommands_ImplementICommand_SoUnitOfWorkCommits`
  断言 4 个命令均可赋值给 `ICommand<>`，防止再次退化。
- 教训（与 F40 的 `ReplayExecutionCommand` 刻意用 `IRequest` 避开提交互为镜像）：
  **写操作命令必须 `ICommand`，只读命令才用 `IRequest`**；handler 级单测断言
  `repo.Update` 被调用并不足以证明落库——必须做一次真实联调或显式锁类型。Id`+`goal`）与前端配置面板字段对齐
- [ ] 既有 `UpdateWorkflow` 整图保存不受影响（回归）
- [ ] 无新增死码 / 未使用 import
