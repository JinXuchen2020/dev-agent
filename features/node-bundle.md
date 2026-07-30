# F20 · 节点全家桶（Workflow 节点类型扩展）

> 状态：`open`。来源：F7 工作流平台化 program 子项 **②**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其是 StepType 枚举扩展与 HITL 审批门方案）。
>
> 定位：把 DAG 画布从「LLM/Agent/Tool/Code/Knowledge」扩展到生产级编排原语。**Tool/Code/Knowledge Retrieval 节点 executor 已在 F5 落地**（StepType.Tool=6 / Code=7 / Knowledge=5），本 feature 仅补其余类型 + 前端调色板/配置面板 + 运行时 executor。

## 0. 目标
让工作流能表达真实编排语义：HTTP 调用外部服务、条件分支、循环、变量读写、嵌套子工作流、延迟等待、人工审批门（HITL）。

## 1. 范围
**in**：
- 新增节点类型：`HTTP` / `Condition` / `Loop` / `Variable` / `SubWorkflow` / `Delay` / `UserInput(HITL)`
- 各节点前端调色板图标 + 配置面板 + 后端 executor（`IWorkflowExecutable` 经 `HandlesType` 路由，沿用 F5 模式）
- `WorkflowGraphSnapshot` / 导入导出（F7 ①）已能携带任意 `StepType`，本 feature 仅需让序列化与校验兼容新类型（补充 `ValidateGraph` 的拓扑规则）

**out（本 feature 不做）**：
- 触发器（见 F21）、发布为 API（F22）、调试器（F25）单步能力 —— 仅提供节点原语，不提供触发/发布/单步 UI

## 2. 接口契约草案（后端）
- 新增 `StepType` 枚举值（连续扩展，避免与现有冲突）：
  `Http=8, Condition=9, Loop=10, Variable=11, SubWorkflow=12, Delay=13, UserInput=14`
- 配置结构（落 `WorkflowNode.ConfigJson`）：
  - `Http`：`{ method, url, headers, bodyTemplate, authRef? }`
  - `Condition`：`{ expression, trueBranch, falseBranch }`（expression = 简易 JSON-path/JS 表达式，引擎待定）
  - `Loop`：`{ itemsSource, itemVariable, bodySubgraphRef }`
  - `Variable`：`{ mode: set|get, name, value }`
  - `SubWorkflow`：`{ workflowId, inputMapping }`
  - `Delay`：`{ durationMs | cron }`
  - `UserInput`：`{ prompt, approvalRole? }`（暂停等待人工恢复）
- `UserInput` 需新端点 `POST /api/v1/workflows/{id}/executions/{execId}/resume`（携带人工输入，复用 `WorkflowConflictException` 式并发守卫），与现有 execution 恢复机制对齐（待确认是否复用 ExecutionLog 暂停态）

## 3. 数据模型与改动面
- `StepType` 枚举扩展（Domain，`WorkflowStepType.cs`）——**破坏性枚举扩展**，所有 `switch`/`HasFlag`/映射需补 default/新分支（重点回归）。
- `IWorkflowExecutable` 新增 7 个 executor 类（`Infrastructure/Workflow/Executors/`），经 `HandlesType` 注册。
- 前端 `appStore` 节点调色板 + 7 个配置面板组件（`WorkflowCanvasPage`/`nodes/`）。
- `ValidateGraph` 增补：Condition 须有 true/false 出边、Loop 须有 body 子图、UserInput 不阻断（可作末端等待）等。
- **无新聚合 / 无 EF 迁移**（节点定义仍存 `Workflow.Nodes` ConfigJson）。

## 4. 风险
- 🔴 高风险：枚举破坏性扩展（全仓 switch 回归）、HITL 审批门需执行引擎支持暂停/恢复、表达式引擎选型。
- 缓解：枚举扩展先行全局 grep `StepType` 补分支；HITL 方案见 §6 S3 先定后做；表达式引擎 v1 用受限安全子集（禁任意代码执行）。

## 5. 验收标准草案
- 各新节点可拖入画布、配置、存为工作流、执行产生正确下游路由与 IO。
- HTTP 节点真实出站（mock transport 单测）；Condition 按表达式真/假走不同分支；Loop 按集合迭代；Variable 跨节点可读写；SubWorkflow 调用目标工作流并返回；Delay 实际等待；UserInput 暂停→人工恢复后继续。
- 导入/导出 JSON 携带新类型节点无丢失（复用 F7 ① 往返测试）。
- 前端 tsc 0 + qa.mjs 全绿 + 7 节点配置面板单测。

## 6. 决策（待锁定，动手前须用户拍板）
- **S1** StepType 枚举扩展命名与起始值（建议自 8 连续，见 §2）。
- **S2** 条件/循环表达式引擎：受限 JSON-path 子集 vs 嵌入式 JS（安全沙箱）vs 复用 `IConditionEvaluator`（若存在）。
- **S3** HITL 审批门：`UserInput` 节点 = 复用现有 Execution 暂停态 + 新 resume 端点，还是引入独立 `HumanApproval` 聚合（影响数据模型与审计）。
- **S4** SubWorkflow 调用：同步嵌套 execution（父子关联）vs 异步独立 execution 引用（影响 Trace 关联，见 F24）。
- **S5** Loop body 表达：子图引用（`bodySubgraphRef`）vs 复制节点（影响快照与导入导出）。
