# 竞品对标与功能差距分析 · Agent 编排平台

> 目的：对比 Dify / n8n / LangGraph / Coze(扣子) / Flowise 等主流平台，识别本平台（AgentPlatform.Web + .NET 后端）需要**新增**与**优化**的功能，形成可用于排期的路线图。
> 配套缺陷清单见 `./backlog.md`（功能缺陷 B1–B6、优化 O1–O14）。
> 生成日期：2026-07-22

---

## 0. 一个必须先讲清的关键结论

**Workflow Editor 不够完善，根因在后端模型，而非单纯前端 bug。**

经后端代码核查：
- 工作流 = `List<WorkflowStep>`（按 `Order` 线性执行），**不存在 Node / Edge / Connection 实体**，不是 DAG。
- 全平台只有 2 个 step executor：`AgentCallStepExecutor`（匹配 `*` = 兜底，本质是「一次 LLM 调用」）和 `CriticStepExecutor`（匹配 `*critic*`，评审步）。**没有代码/HTTP/工具/检索/分支/循环等节点概念。**
- `WorkflowsController` **只有 `GET / POST / GET {id}/progress`，没有 `PUT/PATCH`**。工作流一旦创建即「创建并立即执行」，不可编辑后保存。
- 前端 `/workflows/:id/edit` 调用 `POST` 且忽略 `id` 是「正确行为」——因为后端根本没有更新端点。

**推论**：要做「完善的 Workflow Editor（画布 + 节点 + 编辑保存）」，必须先给后端引入 DAG 模型 + 更新端点，前端才能从「表单式配置」升级为「可视化画布」。这是 P0/P1 的硬前置。

---

## 1. 竞品能力矩阵

图例：✅ 完整 ｜ 🟡 部分/间接 ｜ ❌ 无 ｜ — 不适用

| 能力维度 | Dify | n8n | LangGraph | Coze 扣子 | Flowise | **本平台** |
|---|---|---|---|---|---|---|
| ① 可视化 DAG 画布（拖拽节点+连线） | ✅ | ✅ | ✅(Studio) | ✅ | ✅ | ❌ 表单式 |
| ② 节点类型丰富度 | ✅ LLM/检索/分支/HTTP/代码/迭代/聚合/工具/Agent | ✅ 400+ 集成 + 代码 | 🟡 代码优先图 | ✅ 12+ 节点 | 🟡 | ❌ 仅 LLM + Critic |
| ③ 实时调试（单步/变量监视/节点 IO 预览） | ✅ 变量监视+单步重跑 | ✅ 节点级 IO + 重放 | ✅ 时间旅行 | ✅ 逐节点 IO | 🟡 | 🟡 SSE 有但无 JWT、无变量监视 |
| ④ 工作流版本管理 / 回滚 | ✅ | ✅(Git) | ✅(Assistants) | ✅ | 🟡 | ❌ |
| ⑤ 导入导出（JSON/YAML） | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 仅有 YAML 配置 |
| ⑥ RAG 知识库节点（独立可配） | ✅ 向量/全文/混合+重排 | 🟡 经节点 | 🟡 | ✅ | ✅ | 🟡 隐式注入（每步自动检索，不可配） |
| ⑦ 多智能体编排（角色/协商/监督） | 🟡 Agent 节点 | ✅ 多 Agent AI 节点 | ✅ Supervisor/Swarm | ✅ | 🟡 | ✅ **协商式(Negotiation) — 差异化优势** |
| ⑧ 人机协同 / 审批门 | 🟡 | ✅ HITL | ✅ Interrupt | ✅ Question 节点 | ❌ | 🟡 `NeedsIntervention→Paused` 隐式 |
| ⑨ 错误处理（重试/错误分支/忽略异常） | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡 引擎级重试+回滚，无错误分支节点 |
| ⑩ 触发器（Webhook/定时/Chat/事件） | ✅ | ✅ | 🟡 | ✅(Chat) | 🟡 | ❌ |
| ⑪ 子工作流 / 复用 | ✅ | ✅ | ✅ | ✅ | 🟡 | ❌ |
| ⑫ 模板市场 / 社区 | ✅ | 🟡 | 🟡 | ✅ | ❌ | ❌ |
| ⑬ 发布为 API / MCP Server | ✅(API+MCP) | ✅ | ✅ | 🟡 | ✅(API+MCP) | 🟡 有 API Key，但非工作流级 |
| ⑭ 评估 / 观测 (Tracing) | ✅(Langfuse) | ✅ | ✅(LangSmith) | 🟡 | 🟡 | 🟡 有执行日志，无 trace |
| ⑮ 企业治理（RBAC/审计/SSO） | ✅ | ✅ | ✅ | 🟡 | ❌ | ✅ **Phase 5 安全已落地** |

**本平台相对位置**：企业治理(⑮)与多智能体协商(⑦)是强项、甚至领先；**可视化编排(①)、节点丰富度(②)、调试(③)、版本(④)、触发器(⑩)、子流程(⑪)、模板(⑫) 是明显短板**，正是用户感知「Workflow Editor 不够完善」的来源。

---

## 2. 本平台现状（事实清单）

### 后端（已核查）
- **模型**：`Workflow` 聚合根持有 `List<WorkflowStep>`，每步 `Order`/`State`/`Result`；`WorkflowState` = Pending/Running/Paused/Completed/Failed/RolledBack。
- **执行引擎（真实存在）**：`OrchestrationPrimitive` + `SequentialOrchestrator`（按序执行、重试、回滚、上下文构建、领域事件）+ `NegotiationOrchestrator`（LLM 驱动步骤选择 + 收敛终止，多智能体辩论/评审）。
- **流式**：`GET /api/v1/workflows/{id}/progress` SSE（但前端原生 `EventSource` 无法带 JWT → 鉴权下失效，见 backlog B2）。
- **RAG**：`SequentialOrchestrator.BuildWorkflowContext` 每步调 `IVectorStore.SearchAsync("workflow-context", stepName, topK:3)`——**隐式、不可配置、非节点**。
- **工具**：`ToolDefinition` 聚合（native/skill/MCP，`ParametersSchema`，`EndpointUrl`）已存在，但**未接入工作流节点**。
- **缺失**：DAG 模型、StepType 枚举、PUT 端点、版本、条件分支、通用循环、HITL 审批门、触发器。

### 前端（已核查，详见 backlog.md）
- 编辑器是「YAML/JSON 配置表单」，**非画布**；`/edit` 路由保存即新建（B1）；SSE 无 JWT（B2）；context 解析白屏（B4）。
- Dashboard/ApiKeys/AgentConfigurations 含**硬编码假数据 / mock**（backlog P2）。
- 会话功能「列表是死胡同」，无聊天界面/路由（B5）；登录密码不校验（B6）。
- 缺 ErrorBoundary、401 整页跳转、打包未拆包、单测覆盖 <5%（O1/O2/O6/O7）。

---

## 3. 差距归类（按影响力）

### A. 可视化编排（Workflow Editor 核心差距）— 最高优先
- 无画布、无节点拖拽、无连线、无缩放/缩略图、无撤销重做。
- 后端无 DAG、无 PUT → 编辑/保存链路断裂。

### B. 节点 / 算子丰富度
- 仅「LLM 调用 + 评审」两类。缺：代码(JS/Py)、HTTP、工具调用、知识检索（显式）、条件分支、循环/迭代、变量/聚合、子流程、延迟、User-Input(HITL)。

### C. 调试与可观测性
- 无单步运行、无变量监视面板、无节点级输入输出预览、无 trace。SSE 流式鉴权失效。

### D. 版本与协作
- 工作流无版本/草稿/回滚/导入导出；无多人协作/评论。

### E. 知识 / RAG
- 检索隐式注入且不可配（topK/检索方式/重排/知识库选择均无 UI）。

### F. 多智能体与人机协同
- **优势项（Negotiation/Critic）需产品化**：目前仅后端能力，前端无「协商/智能体团队」编排视图；HITL 仅为隐式 Paused，无可配置审批门。

### G. 集成 / 工具生态 / 发布
- 触发器缺失（Webhook/定时/Chat）；工具注册表未接入工作流；未支持「发布为 API / MCP Server」；无模板市场。

### H. 平台工程（延续 backlog 优化）
- 401 整页跳转、JWT 存 localStorage、租户/用户硬编码、测试覆盖低、打包未拆包、a11y 薄弱。

---

## 4. 建议路线图（分期 + 与 backlog 关联）

### P0 — 修通「编辑/更新」链路（当前阻断项，1–2 周）
**前置：后端必须加 `PUT /api/v1/workflows/{id}`（草稿更新；或拆 `PATCH` 仅改元数据）。**
- [ ] 后端：Workflow 聚合增加 `Update`/`SaveDraft` 路径 + `PUT` 端点（DAG 化前可先支持 ordered-steps 的增删改）。
- [ ] 前端：修复 `/workflows/:id/edit` 调 PUT 并带 id（解决 backlog B1）；「Save & Run」与「保存草稿」拆分为两个按钮。
- [ ] 前端：SSE 鉴权——用 `fetch`+`ReadableStream` 封装带 `Authorization` 的进度流，替代原生 `EventSource`（解决 B2/B3）。
- [ ] 前端：`JSON.parse(wf.context)` 加 try/catch，非法 context 给空态而非白屏（B4）。

### P1 — 可视化 DAG 画布 MVP（核心差距，3–5 周）
**前置：后端引入 Node/Edge/DAG 模型（或 ordered-steps 增加 `DependsOn` 父子依赖，作为 DAG 过渡）。**
- [ ] 后端：新增 `WorkflowNode`/`WorkflowEdge` 聚合 + 图校验（环检测、入口/出口）；`SequentialOrchestrator` 改为按拓扑序执行。
- [ ] 后端：节点 `StepType` 枚举（Start/End/LLM/Agent/Critic/…），executor 按类型路由（替代字符串 glob 匹配）。
- [ ] 前端：画布组件（拖拽放置、连线、框选、缩放/小地图、网格对齐、撤销重做）。
- [ ] 前端：节点配置侧栏（按 StepType 渲染表单）；基础节点 Start/End/LLM/Agent(分配)/Critic。
- [ ] 调试：节点级「试运行」+ 变量监视面板（前端先用内存态模拟，后端补单步 `RunStepAsync`）。

### P2 — 节点全家桶 + 调试器 + 版本（6–10 周）
- [ ] 节点：Code(JS/Py)、HTTP Request、Tool（接入 `ToolDefinition`）、Knowledge Retrieval（显式节点：选知识库 + topK + 检索方式 + 重排）、Condition/If-Else、Loop/Iteration、Variable/聚合、Sub-workflow、Delay、User-Input(HITL 审批门)。
- [ ] 调试器：Dify 式变量监视 + 节点级重跑（不重跑上游）+ 错误分支 /「忽略异常」默认输出。
- [ ] 版本：Workflow 版本快照 + 回滚 + 导入/导出 JSON（对齐 Coze/Dify）。
- [ ] HITL：可配置审批节点（替代隐式 Paused），支持批准/拒绝/改输入后继续。
- [ ] 触发器：Webhook / 定时(cron) / Chat 触发（对齐 n8n/Coze）。

### P3 — 生态与发布（10 周+）
- [ ] 发布为 API：为每个工作流生成可调用的 REST 端点 + 复用现有 API Key；探索「发布为 MCP Server」（后端已有 MCP `ToolDefinition` 基础，复用成本低）。
- [ ] 模板市场 / 示例库：内置 5–10 个行业模板（客服、研报、内容生成…）一键克隆。
- [ ] 评估/观测：执行 trace 视图（节点级耗时/token/IO），数据集回归评估（对标 LangSmith/Langfuse）。
- [ ] 企业增强：多工作空间、用量仪表盘、工作流 diff（Git 式）。

### 保留的差异化优势（不要丢）
- **协商式多智能体 + Critic 收敛**：这是 Dify/n8n 都没有的原生原语。P1 画布应提供「Agent-Team / Negotiation」专属模式（多个 Agent 节点 + Critic 节点 + 收敛终止条件），把它做成卖点而非被线性画布稀释。

---

## 5. 落地优先级建议（一句话）

1. **先打通 P0（PUT + 编辑/流式鉴权）**——这是用户当下「Editor 不能用」的直接原因，成本最低、获得感最强。
2. **再投 P1 画布 MVP**——这是与竞品拉开体验差距的支点，但必须配套后端 DAG 模型，不能只做前端假画布。
3. **P2 节点全家桶顺着 DAG 模型自然扩张**，每加一种节点 = 加一个 executor + 一个前端节点组件。
4. **P3 把已有的 Phase 5 安全、API Key、MCP/Tool 基础「接出」到工作流层**，形成闭环。

> 关联 backlog：B1/B2/B3/B4 属 P0；O1–O14 中画布/调试/版本/触发器/打包/测试分别映射到 P1–P3。新增功能意图已同步写入 `./backlog.md` 第五节，供 feature-dev 自动消费。
