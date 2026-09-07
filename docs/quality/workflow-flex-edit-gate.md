# 质量门 · 工作流灵活编排（#1 Agentic 节点入画布 + #2 增量节点/连线 API）

- 分支：`feat/workflow-flex-edit`（基于 `e456ece`）
- 设计文档：`features/workflow-flex-edit.md`
- 日期：2026-09-07
- 类型：聚焦式 feature（非阶段收尾）

## 1. 变更范围

| # | 改动 | 文件 |
| :--- | :--- | :--- |
| 1 | `StepType.Agentic = 15`（对齐后端枚举 `StepType.cs:55`）+ `NodeConfig.goal` | `src/types/index.ts` |
| 2 | store 四处映射补全（节点种类 ↔ StepType、后端名、默认配置） | `src/stores/workflowCanvasStore.ts` |
| 3 | 调色板新增 Agentic 条目 + 文案/描述 | `src/components/canvas/NodePalette.tsx` |
| 4 | 节点图标（`RobotOutlined`） | `src/components/canvas/DagNode.tsx` |
| 5 | 配置面板：Agent 下拉复用 + Goal 文本域 + 说明；节点类型标签 | `src/components/canvas/NodeConfigPanel.tsx` |
| 6 | 画布 `nodeTypes` 注册 `agentic` | `src/pages/WorkflowCanvasPage.tsx` |
| 7 | `Record<StepType, …>` 全键约束补齐（漏改则 tsc 报错） | `src/components/canvas/VariableWatchPanel.tsx` |
| 8 | i18n 双语文案（nodeType/nodeDesc + 4 个配置键） | `src/locales/zh-CN.ts`、`en-US.ts` |
| 9 | 领域方法 `AddNode/AddEdge` 改为返回新建实体（回传服务端 Id） | `Domain/Aggregates/Workflows/Workflow.cs` |
| 10 | 4 个增量 Command/Handler（租户校验 + 运行态锁） | `Application/Workflows/Commands/{Add,Remove}Workflow{Node,Edge}/` |
| 11 | 4 个端点（Admin,Operator） | `Api/Controllers/WorkflowsController.cs` |
| 12 | 前端类型化客户端函数 ×4 | `src/services/api.ts` |
| 13 | 11 项 handler 单元测试 | `Application.Tests/Handlers/WorkflowNodeEdgeHandlersTests.cs` |
| 14 | api-spec 端点表 | `appendices/api-spec.md` |

## 2. 关键设计决策与风险边界

- **画布保存链路不改**：store 以客户端 UUID 为 React Flow id 真相源，走整图 `PUT /workflows/{id}`
  （后端 `ReplaceGraph` 按 id 重映射）。若同时启用增量端点会产生重复节点，故本轮增量 API
  作为**程序化/后续画布改造的能力底座**落地，UI 仍走整图保存——避免 destabilize 正在工作的编辑器。
- **增量编辑不调用 `ValidateGraph`**：局部图在编辑中间态天然不合法（如孤立节点），强制全量校验
  会让"一步步搭图"无法进行；全量校验仍由运行端与整图保存路径把关。
- **运行态锁一致**：与 `UpdateWorkflowCommand` 相同，`Running`/`Paused` 抛 `WorkflowConflictException`
  （→409）；租户不匹配/不存在统一返回 404（不泄露存在性）。
- **`AddNode`/`AddEdge` 签名变更**：grep 证实既有调用点仅测试与新增 handler，无其他生产调用。

## 3. 校验结果

| 项 | 结果 |
| :--- | :--- |
| `dotnet build src/AgentPlatform.sln` | **0 警告 0 错误** |
| Application.Tests | **297 通过**（基线 285 → +12 新增，含 1 项 `ICommand` 回归锁），0 失败 |
| Infrastructure.Tests | 175 通过 / 8 跳过，0 失败 |
| Api.Tests | 39 通过，0 失败 |
| ArchitectureTests | 9 通过，0 失败 |
| `tsc --noEmit` | **0 error** |
| `bddgen` | exit 0 |
| `vitest` | 50 通过；**1 失败为既有**（i18n-symmetry「搭建 Agent 团队」）+ 1 文件级既有失败（AgentsPage.contract mock 缺 `WORKSPACE_STORAGE_KEY` export），均不在本次改动面 |
| i18n 键对称性 | 新增 6 键 × 双语齐全（`agentic`/`agenticGoal`/`agenticGoalTooltip`/`agenticGoalPlaceholder`/`agenticHint`/`agenticAgentTooltip`） |
| 真实联调（curl + cookie 登录，本地 Integration 后端） | 加 Agentic 节点→详情可见（type=15）；加边→详情可见；删边 204；删节点 204；不存在的边 404 |
| 本地 E2E（Integration + Edge） | **本环境不可跑**：vite dev server / `vite build` 启动即被 CLI 注入的 `node-safe-delete-shim` 阻断（清理 `node_modules/.vite` 与 `dist` 触发批量删除守卫），沙箱放行亦无效 → 交 CI |

## 4. 实现期发现并修复的 P0：命令未实现 `ICommand<T>` → 变更不落库的「假成功」

初版 4 个命令声明 `IRequest<T>`，而 `UnitOfWorkBehavior` 约束 `where TRequest : ICommand<TResponse>`
（`Application/Abstractions/ICommand.cs`）→ **不触发 `SaveChanges`**。端点返回 200 + 服务端 Id，
但数据库无变化；**仅靠 handler 级单测（断言 `repo.Update` 被调用）发现不了**，
由 curl 真实联调（POST 后重新 GET 详情看不到新节点）暴露。

- 修复：4 个命令改 `ICommand<WorkflowNodeResponse?>` / `ICommand<bool>` /
  `ICommand<WorkflowEdgeResponse?>` / `ICommand<bool>`，并补 `using AgentPlatform.Application.Abstractions;`。
- 回归锁：`WorkflowNodeEdgeHandlersTests.IncrementalGraphCommands_ImplementICommand_SoUnitOfWorkCommits`。
- 规则沉淀：**写操作命令必须 `ICommand`，只读才用 `IRequest`**（与 F40 `ReplayExecutionCommand`
  刻意用 `IRequest` 避开提交互为镜像）。

## 5. 遗留与诚实声明

- **增量端点未经 UI 验证**：画布不调用新端点，故 E2E 覆盖不到；仅有 handler 级单测。
  真实联调留待画布改造为增量保存时进行。
- **SpecFlow 本地不可跑**：需真实 `OPENAI_API_KEY`（`IntegrationAppFactory` 启动即校验），
  本地 shell 无 key；SpecFlow 无针对新端点的场景。
- **既有文档漂移（未修，非本次引入）**：`appendices/api-spec.md:405` 的 `nodeType` 注释仍为旧枚举
  （0=Start…10=Transform），与 `StepType.cs` 实际定义（含 15=Agentic）不一致，建议单独修。
