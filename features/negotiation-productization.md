# F8 · 差异化优势产品化（Negotiation + Critic）前端专属模式

> 史诗状态：**doing**（feature-builder 流水线端到端实现中）
> 分支：`feat/f8-negotiation-productization`
> 优先级：`[native]`（平台差异化壁垒，Dify/n8n 无此原生原语）
> 风险：`🟢 低风险`（纯前端产品化，后端原语已就绪，无破坏性改动、无契约变更）

## 1. 目标

把后端已具备的 **协商式多智能体（Negotiation）** 与 **评审收敛（Critic）** 原语，从「隐形能力」变为画布上**显式、可发现、可引导**的「Agent-Team / Negotiation」专属模式：

- 多 Agent 节点（Architect / Developer …）+ Critic 评审节点 + 收敛终止条件，开箱即用。
- 用户能**显式选择**编排模式（自动识别 / 顺序 / 协商），而不仅靠隐式自动识别。
- 画布**可见**协商模式指示，让用户知道当前图会以 Critic 收敛方式运行。
- 一键**脚手架**生成多 Agent 协商图，降低上手门槛。

## 2. 现状核验（2026-08-11 实采代码）

**后端原语已完整就绪（无需改动）：**

- `OrchestrationPreset` 枚举：`Sequential=0` / `Negotiation=1`（`src/AgentPlatform.Application/Abstractions/OrchestrationPreset.cs`）。
- `OrchestrationPrimitive.RunAsync` 已分支：`case OrchestrationPreset.Negotiation → _negotiation.RunNegotiationAsync(...)`（`src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs:154`）。
- `DetectPreset(workflow)`：图含 `StepType.Critic` 节点即自动判为 Negotiation（`OrchestrationPrimitive.cs:324-332`）→ 现有「无预设」运行已能在含 Critic 时走协商。
- `NegotiationOrchestrator`：`RoleBasedSelectionStrategy`（LLM 驱动选步）+ `CriticConvergenceTermination`（Critic 判 Approved 即收敛，或达 `MaxRounds=20` 硬上限终止）（`DependencyInjection.cs:355-356` 已注册）。
- `CriticStepExecutor`：处理 `StepType.Critic`，调用 `IModelClient` 做结构化评审（Approved/Feedback/ReworkTarget/Diff），模型不可用时按 `AllowCriticOverride` 回退（`CriticStepExecutor.cs`）。
- API 已接受预设：`RunExistingWorkflowRequest(Preset?)`、`WorkflowsController.RunExistingWorkflow` 读 `request?.Preset`（`WorkflowsController.cs:160-172`）。

**前端 Critic 节点已就绪（无需改动）：**

- `StepType.Critic = 4`（`types/index.ts:151`）；`NodePalette` 调色板、`DagNode` 图标、`NodeConfigPanel` 配置面板、`STEP_TYPE_TO_NODE_TYPE`/`NODE_TYPE_TO_STEP_TYPE` 映射、`VariableWatchPanel`、`WorkflowDiffModal`、`zh-CN`/`en-US` i18n 均已包含 Critic。
- `runExistingWorkflow(id, preset?)` 已声明预设参数（`api.ts:191`）。

**结论：** F8 的剩余工作 100% 落在前端「产品化」层——把既有的 Negotiation/Critic 能力以**可显式控制、可见、可引导**的方式暴露给用户。后端零改动、零迁移、零契约变更。

## 3. 范围边界（硬约束：纯前端、不触后端）

| 做 | 不做 |
|---|---|
| 画布工具栏：编排模式 `Segmented`（自动/顺序/协商） | 改后端枚举 / 新增端点 / 改 `DetectPreset` |
| 画布「协商模式」可见指示（含 Critic 节点或显式选协商时） | 改 `CriticConvergenceTermination` 收敛逻辑 |
| 「搭建 Agent 团队」脚手架按钮（生成多 Agent 协商图） | 改 `CriticStepExecutor` 评审语义 |
| 模型一致性：preset 以 **int** 收发（API 未注册 `JsonStringEnumConverter`，`Negotiation=1`） | 把枚举改成字符串序列化（影响全仓契约，越界） |
| BDD E2E：脚手架→含 Critic→协商可见→保存运行→终态 | — |

## 4. 前后端接口契约

### 4.1 运行预设（复用既有端点，无新增）

- `POST /api/v1/workflows/{id}/run`，body `{ "preset": <int> }`，省略则后端 `DetectPreset`。
- 枚举线格式（**int**，因全局未注册 `JsonStringEnumConverter`）：`Sequential=0`、`Negotiation=1`。
- 前端 `runExistingWorkflow(id, mode)`：`mode='auto'` → 不发 preset（后端自动识别）；`'sequential'` → `{preset:0}`；`'negotiation'` → `{preset:1}`。
- 新建工作流路径（`runWorkflow` 线性步骤创建）不改预设；其图含 Critic 时后端自动识别 Negotiation，符合预期。

### 4.2 数据模型（前端）

- `workflowCanvasStore` 新增动作 `scaffoldAgentTeam()`：一次性构建 DAG——`Start → Architect(Agent) → Developer(Agent) → Critic → End`，节点配置取 `defaultConfig`（Agent=`{agentId:null}`、Critic=`{criteria:''}`、End=`{summary:'all'}`），节点名唯一、无环、从 Start 全连通 → 通过 `ValidateGraph`。
- 组件本地状态 `presetMode: OrchestrationPresetMode`（`'auto'|'sequential'|'negotiation'`，默认 `'auto'`）。
- 协商模式指示派生：`presetMode==='negotiation' || nodes.some(n => n.data.stepType===StepType.Critic)`。

## 5. 验收标准

- **A1** 画布工具栏出现「编排模式」`Segmented`（自动/顺序/协商），默认自动。
- **A2** 图含 Critic 节点（或显式选协商）时，画布显示「协商模式 · 评审收敛」指示。
- **A3** 「搭建 Agent 团队」按钮：点击后画布出现 5 个节点（Start/Architect/Developer/Critic/End）且 4 条有向边连通。
- **A4** `handleSaveAndRun` 在已有工作流时把 `presetMode` 映射为 int 传入 `runExistingWorkflow`（经 `qa.mjs` + 单元断言验证不发错预设值）。
- **A5** 模型一致性：`tsc --noEmit` 0 error；前端 `OrchestrationPresetMode` 与后端 `OrchestrationPreset` 语义对齐（auto=省略/0/1）。
- **A6** BDD E2E（`@e2e`）：登录 admin → `/workflows/new` → 点「搭建 Agent 团队」→ 断言含 Critic 节点 + 协商模式可见 → 保存并运行 → 工作流达终态（Completed）且无意外 JS/HTTP 错误。
- **A7** 既有测试不回归：`dotnet test` 全绿（后端零改动）、前端 `vitest` + `vite build` 通过、三道质量门 0 open。

## 6. Phase Quality Gate Checklist（嵌入）

> 供 `ddd-phase-quality-gate` 消费；P0/P1/P2/P3 须 0 open 方过门。

- **P0（阻断）**
  - [ ] 预设以 int 收发，无字符串枚举误用导致 400 / 静默忽略。
  - [ ] 脚手架生成图通过 `ValidateGraph`（1 Start / ≥1 End / 无环 / 全连通 / 名称唯一）。
  - [ ] 新建/保存运行不破坏既有工作流（既有 BDD 不回归）。
- **P1（高）**
  - [ ] 协商模式指示在「含 Critic」与「显式选协商」两种条件下均正确显示/隐藏。
  - [ ] 编排模式 `Segmented` 三态语义正确（auto=后端识别；sequential/negotiation 映射 0/1）。
  - [ ] i18n zh-CN / en-US 键对称（新增 canvas.preset* / negotiationMode / scaffoldAgentTeam* 两语言齐备）。
- **P2（中）**
  - [ ] 脚手架按钮置于工具栏且显著，含 tooltip 说明其生成多 Agent 协商图。
  - [ ] `scaffoldAgentTeam` 走单一 history 快照（支持撤销）。
  - [ ] 代码无 `any`、无 TODO/FIXME、lint 净。
- **P3（低）**
  - [ ] 组件拆分合理，preset 逻辑不污染 `handleSaveAndRun` 主路径。
  - [ ] 注释解释「int 枚举」契约缘由（防后人误改字符串）。

## 7. 风险与缓解

- **模型一致性（低）**：API 未注册 `JsonStringEnumConverter`，预设须以 int 收发。缓解：前端 `runExistingWorkflow` 显式映射 `negotiation→1`/`sequential→0`，并在注释标注；单测覆盖映射。
- **E2E 协商收敛确定性（低）**：Integration 环境 `ModelClient:StubResponse="Integration test stub response."` 非 Critic JSON，且 `AllowCriticOverride=false` → Critic 单次判 Approved=false 后仍按「无可选步」路径完成（不无限循环）。E2E 仅断言工作流达 **Completed** 终态 + 无意外错误，不耦合具体评审结果。
- **脚手架图合法性（低）**：严格按 `ValidateGraph` 构造（单 Start、单 End、线性无环、名称唯一）。

## 8. 文档同步范围（Phase 6）

- `CHANGELOG.md`：顶部补 F8 版本条目。
- `docs/AGENT_PLATFORM_BLUEPRINT.md` / `appendices/*`：若有「Agent-Team / Negotiation 模式」相关描述，补前端产品化说明（后端原语部分已存在，仅补可见性/脚手架）。
- `features/backlog.md`：F8 标 `done`。
- `docs/quality/f8-negotiation-gate.md`：质量报告。
