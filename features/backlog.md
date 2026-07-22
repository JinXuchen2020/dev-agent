# 功能待办池（backlog）

> **位置与定位**：仓库根 `features/backlog.md`（与 `src` 平级）。本池同时收纳**前端与后端**的 feature 设计与实现意图；**新增 feature 须先将设计文档放入 `features/`，再进入实现**（见下方红线）。
>
> 本文件是两用的：
> 1. **全量代码分析的缺陷 / 优化清单**（见一~三节）——由人工或 `codebase-optimizer` 消费，作为迭代排期依据。
> 2. **feature-dev 意图池**（见四、五节）——feature-dev 流程从池顶取 `open` 任务自主实现，五道 QA 闸门全绿后标 `done`。
>
> **红线**：代理**绝不**自己发明需求。只实现这里列出的、或你当轮明确指令的功能。涉及接口契约 / 鉴权 / 路由结构等高风险改动时，代理会停下问人，不自动改。

状态图例：`open`(待做) · `doing`(进行中) · `done`(已完成) · `blocked`(阻塞等待依赖)

代码基现状（2026-07-22 全量走查）：React 19 + Vite 8 + TS（strict）+ Antd 5 + @xyflow/react + zustand；29 个源文件；`typecheck/lint/build/unit/e2e` 五道闸门当前全绿（qa-report.json）。**优点**：严格 TS、0 处 `any`、0 TODO/FIXME、lint 净、契约测试保护了 Agents 列映射。**问题**：核心实时/编辑链路有多处功能性缺陷，Dashboard/ApiKeys 含硬编码假数据，全站缺错误边界与统一错误态，打包未拆包。

---

## 一、功能缺陷（Bug / 数据·逻辑错误）

> 会直接导致功能不可用或数据错误，优先修。

- **[P1] B1 · 工作流编辑器「编辑模式」失效，且保存即运行**
  - 位置：`src/pages/WorkflowEditorPage.tsx:28-29,90-110`
  - 症状：`/workflows/:id/edit` 进入后会从后端把工作流加载进画布，但 `handleSave` 始终调用 `runWorkflow`（POST `/workflows`）并**忽略 `id`** → 等于新建一条工作流而非更新原工作流；按钮叫「Save & Run」会**立即执行**而非仅存草稿。
  - 修复：编辑态应调 `PUT /workflows/{id}`（若后端支持）或在 UI 明确「另存为新工作流」；拆分「保存草稿」与「运行」两个动作；保存前校验至少 1 个 step。
  - 状态：done（2026-07-22，后端加 `PUT /api/v1/workflows/{id}` + 编辑态拆分「保存草稿/运行」；见 `docs/quality/p0-workflow-update-gate.md`）

- **[P1] B2 · 实时进度 SSE 无法携带 JWT，鉴权下完全失效**
  - 位置：`src/pages/WorkflowDetailPage.tsx:47`、`src/pages/ExecutionLogDetailPage.tsx:42`
  - 症状：原生 `new EventSource('/api/v1/workflows/{id}/progress')` 浏览器**不允许设置 Authorization 头**；Phase 5 开启 JWT 后 SSE 被 401 拒绝，运行中的工作流实时进度/日志更新全部收不到。
  - 修复：后端改走鉴权 Cookie，或前端用 `?token=` 查询参数（需后端支持），或改用带 `Authorization` 的 `fetch` + `ReadableStream` 轮询。（顺带可解 O8 的 XSS 问题）
  - 状态：done（2026-07-22，前端改 `fetch`+`ReadableStream` 带 `Authorization: Bearer`，后端 `StreamProgress` 已 `[Authorize]`；见 `docs/quality/p0-workflow-update-gate.md`）

- **[P1] B3 · WorkflowDetail SSE 出错时无限重连刷屏**
  - 位置：`src/pages/WorkflowDetailPage.tsx:79-82`
  - 症状：`es.onerror` 仅 `console.warn` 不 `close()`，而 EventSource 默认自动重连；鉴权失败等场景会无限重试、狂刷 console。对比 `ExecutionLogDetailPage:54` 的 `onerror` 已 `close()`，两处不一致。
  - 修复：统一在 `onerror` 中 `close()`，必要时 toast 提示连接失败。
  - 状态：done（2026-07-22，fetch 流在 unmount 时 `AbortController.abort()` 且非 2xx 直接返回，不再无限重连；见 `docs/quality/p0-workflow-update-gate.md`）

- **[P1] B4 · WorkflowDetail 解析 context 可能整页白屏**
  - 位置：`src/pages/WorkflowDetailPage.tsx:124`
  - 症状：`JSON.parse(wf.context)` 在后端返回空串 / 非 JSON 时抛异常，无 try/catch，整个详情页崩溃。（叠加 O1 无 ErrorBoundary 会白屏）
  - 修复：`try/catch` 包裹，非法/空时显示原始文本或占位，不阻断页面其余部分。
  - 状态：done（2026-07-22，`JSON.parse(wf.context)` 已包 try/catch 回退原始文本；见 `docs/quality/p0-workflow-update-gate.md`）

- **[P1] B5 · 会话（Conversations）功能不可用**
  - 位置：`src/pages/ConversationsPage.tsx`（列表）、`src/App.tsx:51`（无详情路由）、`src/services/api.ts:98`（`sendMessage` 已实现但无 UI 调用）
  - 症状：「新建会话」只能建空会话；前端**没有会话详情/聊天界面**，路由也无 conversation 详情页；`sendMessage` API 已实现但点不进去、发不了消息。会话列表是死胡同。
  - 修复：新增 `/conversations/:id` 详情路由 + 消息气泡 + 输入框，接 `sendMessage`；列表行可点击进入。

- **[P1] B6 · 登录密码形同虚设**
  - 位置：`src/pages/LoginPage.tsx:14,87`
  - 症状：`password` 仅受控展示，从不校验也不随请求发送；任意密码（含空）都能登录。
  - 修复：真实凭证登录须校验密码；dev-login 演示模式下应禁用密码框或明确标注「演示，密码不参与校验」。

### RAG / 后端缺陷（R1–R4，来自 `./rag-design.md`）

> 后端 RAG 骨架真实可跑（`IVectorStore` / `PgVectorStore` / 三接线点），但作为功能不成立。以下为阻断点，整改设计见 `./rag-design.md`。

- **[P1] R1 · 知识库无入库通道（RAG 静默 no-op）**
  - 位置：`src/AgentPlatform.Application/Abstractions/IVectorStore.cs`（`IngestDocumentAsync` 仅定义）、`src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs`（仅实现）、无任何 controller/handler/job 调用
  - 症状：全仓无 `IngestDocumentAsync` 的运行时调用方；`default` 与 `workflow-context` 两集合**永远为空** → 三处 `SearchAsync` 全返回 0 → 生产环境 RAG 是静默 no-op（会话侧 `if (docs.Count > 0)` 直接跳过，连报错都没有）。Phase 4 验收只验了 store 自身，漏验「有路径入库」。
  - 修复：新增 `KnowledgeBase` 聚合 + 上传/切分端点（见 rag-design §2.1）；质量门验收须含「入库端点存在且被调用、入库后 Search 返回 >0」。状态：open（blocked：需后端模型）

- **[P1] R2 · 向量检索无租户隔离（多租户互查知识）**
  - 位置：`src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs`（`document_embeddings` 表无 `tenant_id` 列，`SearchAsync` WHERE 仅按 `collection_name` 过滤）、`IVectorStore` 接口无 `tenantId` 参数
  - 症状：Phase 5 已落地真实多租户（`AppDbContext.HasQueryFilter`），但向量层无隔离 → 租户 A 能检索到租户 B 的知识，属数据泄漏。
  - 修复：`document_embeddings` 加 `tenant_id` 列；`IVectorStore.SearchAsync` 增 `tenantId` 参数；WHERE 加 `AND tenant_id = @tenantId`；三调用方传 `TenantProvider.GetTenantId()`；加跨租户回归测试。状态：open（high-risk：多租户安全）

- **[P1] R3 · RAG 部署强耦合，SQLite 默认部署触发 500**
  - 位置：`src/AgentPlatform.Infrastructure/DependencyInjection.cs`（`PgVectorStore` 无条件注册）、`SendMessageCommandHandler.cs`（检索路径未 try/catch）
  - 症状：`PgVectorStore` 要求 `ConnectionStrings:PostgreSQL` + pgvector + `OpenAI:Key`，但默认 `Database:Type = sqlite`；SQLite 部署下 RAG 首次触发即抛 `InvalidOperationException`，会话路径不捕获 → 直接 500；工作流路径虽 try/catch 静默降级，但「看起来没接地」。
  - 修复：条件注册 + `InMemoryVectorStore` 回退（rag-design §2.3 选 ①）；会话 `SearchAsync` 包 try/catch 降级。状态：open

- **[P2] R4 · 检索无相关性阈值 + 工作流 RAG 语义错位**
  - 位置：`IVectorStore.SearchAsync`（无 `minScore`）、`SequentialOrchestrator` / `NegotiationOrchestrator`（用 `currentStep.StepName` 搜 `workflow-context`）
  - 症状：对话/工作流把**全部召回**（不看 `Score`）灌入 prompt，低分噪声被一并注入；工作流侧 `workflow-context` 实为「步骤间上下文复用」而非用户理解的「外部知识检索」，语义需先定清。
  - 修复：`SearchAsync` 加 `double? minScore`，`PgVectorStore` WHERE 加相似度下限；外部知识检索走独立节点（见五节「节点全家桶·Knowledge Retrieval」）。状态：open

---

## 二、功能缺口 / 数据正确性

- **[P2] B7 · Dashboard 大量指标为硬编码假数据**
  - 位置：`src/pages/DashboardPage.tsx:13-28,66-69`
  - 症状：`trendData`（7 天趋势）、`activity`（最近动态）、「今日会话 248」「Agents +3 本周新增」「Workflows 2 个运行中」全是写死常量，与真实拉取的 `agentCount/workflowCount/successCount` 混排，严重误导运营判断。
  - 修复：趋势/动态/今日会话改为真实接口（如 ExecutionLogs/Conversations 统计）或明确标注「示例数据」；至少把假数字与真实卡区分来源。

- **[P2] B8 · ApiKeys 页全为 Mock + 死按钮**
  - 位置：`src/pages/ApiKeysPage.tsx:58,43-48`、`src/services/api.ts:116-158`
  - 症状：「+ 新建 Key」无 `onClick`；轮换/吊销仅 `message.info` 提示；`getApiKeys` 返回写死数据。
  - 修复：后端端点就绪后真实化（已知阻塞，见四节意图池 P2）。

- **[P2] B9 · AgentConfigurations 的 YAML 内容从不展示**
  - 位置：`src/pages/AgentConfigurationsPage.tsx:11-17`
  - 症状：`yamlContent` 字段已建模，但列表只显示名称/类型/版本；点击行无详情、无 YAML 预览——配置的核心价值对用户不可见。
  - 修复：加展开行或详情抽屉展示 `yamlContent`（语法高亮可选）。

- **[P2] B10 · 状态筛选枚举可能大小写不匹配后端**
  - 位置：`src/pages/ExecutionLogsPage.tsx:56-61`（待核实后端枚举）
  - 症状：筛选值用 `running/completed/failed/rolledback`（小写）；Agent.status 用小写，但 Workflow.currentState / ExecutionLog.status 后端枚举可能为 PascalCase（如 `Running/Completed`）。若后端区分大小写，筛选会返回空。
  - 修复：核对后端状态枚举并统一（建议前端建状态映射表，不裸传字面量）。

- **[P2] B11 · Workflows「快速运行」无错误处理且可能建空工作流**
  - 位置：`src/pages/WorkflowsPage.tsx:27-33`
  - 症状：`handleRun` 无 try/catch，`runWorkflow` 抛错会变成 unhandled rejection；且 `initialContext:'{}'` 不带 steps，若后端要求 steps 会建出空工作流；空名称时 `handleRun` 直接 `return` 且不给提示，模态框卡住。
  - 修复：加错误处理 + 失败 toast；空名称时 `message.warning`；明确空步骤是否允许。

---

## 三、健壮性 / 安全 / 性能 / 可维护性 优化

- **[P1] O1 · 全站缺少 ErrorBoundary**
  - 位置：`src/App.tsx`（顶层无包裹）
  - 症状：任一页面渲染抛错（如 B4 的 `JSON.parse`）会整页白屏无兜底。
  - 修复：App 顶层加 React ErrorBoundary（含「返回首页 / 重试」）。

- **[P1] O2 · 401 处理用整页跳转破坏 SPA**
  - 位置：`src/services/api.ts:34-43`
  - 症状：响应拦截器在 401 时 `window.location.href = '/login'` 触发整页刷新，丢失 SPA 状态/动画。
  - 修复：通过回调或自定义事件通知路由层用 `<Navigate>` 跳转；或暴露可注入的 `navigate`。

- **[P2] O3 · 鉴权态不一致（demo 登录路径）**
  - 位置：`src/pages/LoginPage.tsx:26-33`、`src/stores/appStore.ts:25`
  - 症状：`devLogin` 失败时 catch 分支走「本地演示登录」只调 `login(email)` 但**不写 `auth_token`** → `isAuthenticated=true` 却无 `Authorization` 头，后续请求 401 → 拦截器清 token + 整页跳登录（见 O2），体验闪跳且 demo 模式实际不可用。
  - 修复：demo 路径也写一个占位 token，或在 store 区分 `demo` 标记并在 demo 下跳过 401 跳转。

- **[P2] O4 · 用户/租户信息硬编码且不同源；顶栏搜索/租户切换是装饰**
  - 位置：`src/stores/appStore.ts:21-23`、`src/layouts/AppLayout.tsx:148,191-210`
  - 症状：`tenantName:'Acme Corp'` 写死；`userEmail` 仅由 token 是否存在推断为 `admin@acme.io`；`devLogin` 返回的 tenant/email 未回写；顶栏搜索框、租户切换是纯静态 `div`（不可交互）。
  - 修复：登录后从 JWT payload 或 dev-login 响应写入真实 tenant/user；搜索/租户切换接真实能力，否则先禁用避免误导。

- **[P2] O5 · API 错误被静默吞没，无错误态**
  - 位置：多页（如 `DashboardPage.tsx:39-44`、`ConversationsPage.tsx:18-20`、`AgentRolesPage.tsx:35-37` 等）
  - 症状：多数页面 `.catch(() => {})` 仅停 loading，失败时不展示任何错误态；用户看到空表 / 0 数，无从得知是接口挂了。
  - 修复：统一错误态（Alert / Result + 重试按钮），至少 Dashboard 与列表页。

- **[P2] O6 · 打包体积过大未拆包**
  - 位置：`qa-report.json`（build 段已告警）、`vite.config.ts`（无 `build` 配置）
  - 症状：单 chunk 1.38MB（gzip 435KB）；`@xyflow/react`（仅编辑器用）、`antd` 全进主包。
  - 修复：路由级 `React.lazy` 懒加载 + `manualChunks` 拆 antd/xyflow；Editor 单独懒加载。

- **[P2] O7 · 单元测试覆盖极低**
  - 位置：`src/test/*`、`e2e/*`
  - 症状：单测仅 4 文件（`statusTone`/`StatusBadge`/`AgentsPage` 契约），覆盖 <5% 源码；13 个页面仅 AgentsPage 有契约测试，Dashboard/Workflows/ExecutionLogs/Login/Conversations/编辑器均无单测；e2e 仅 3 个冒烟。
  - 修复：补关键页单测 + 组件测试，至少覆盖数据获取 / 错误态 / 表单提交 / SSE 解析。

- **[P3] O8 · JWT 存 localStorage 有 XSS 风险**
  - 位置：`src/services/api.ts:22`、`src/stores/appStore.ts:27`
  - 症状：与 Phase 5 安全加固基调不符；建议改为 httpOnly + SameSite Cookie，SSE/鉴权一并受益（顺带解 B2）。
  - 说明：属较大改造，需后端配合，排期处理。

- **[P3] O9 · antd 静态 `message` 警告 / 上下文丢失**
  - 位置：多页（`LoginPage.tsx:3`、`AgentsPage.tsx:2` 等）
  - 症状：直接用 `import { message } from 'antd'` 静态方法，antd v5 在 `<App>` 上下文外调用会丢主题且可能告警。
  - 修复：改用 `App.useApp()` 获取 `message`/`modal` 实例。

- **[P3] O10 · 死代码 / 未用能力**
  - 位置：`src/stores/appStore.ts:7-8,20,24`（`sidebarCollapsed`/`toggleSidebar`/`setTenant` 从未消费）；`src/layouts/AppLayout.tsx:178-210`（搜索框、租户切换无交互）；`src/pages/WorkflowEditorPage.tsx:78-88`（节点不可编辑/删除，新增节点 label 只能「Step N」）
  - 修复：删死代码或补交互；编辑器支持节点重命名/删除/连线编辑才有实用价值。

- **[P3] O11 · 无 404 兜底路由；生产态文档链接失效**
  - 位置：`src/App.tsx:45-63`、`src/pages/DashboardPage.tsx:59`
  - 症状：未知路径无 catch-all → 白屏；Dashboard「查看文档」`window.open('/scalar')` 依赖 dev proxy，生产构建（API 异源）下 404。
  - 修复：加 `*` 路由到 NotFound；文档链接用配置化的 API base。

- **[P3] O12 · 列表分页与后端 totalCount 不一致**
  - 位置：`src/pages/ExecutionLogsPage.tsx:70-77`（及 Agents/Workflows 等同构页）
  - 症状：Table `pagination` 在已拉取切片上分页，但 `getXxx` 多数未传 `skip/take`，依赖后端默认 take；`totalCount` 被忽略，大数据量下「分页」只覆盖首段。
  - 修复：接入服务端分页（传 `skip/take` + 用 `totalCount`）。

- **[P3] O13 · 无请求取消（AbortController）/ 卸载后 setState 风险**
  - 位置：多页 `useEffect` 数据获取
  - 症状：快速切页/筛选可能触发对已卸载组件的 setState（React 警告）及重复请求。
  - 修复：用 `AbortController` + effect cleanup。

- **[P3] O14 · 可访问性薄弱**
  - 位置：`src/layouts/AppLayout.tsx:84-113,178-192`
  - 症状：导航项为 `<div onClick>`（键盘不可达）、搜索框为静态 `div`（不可聚焦）、图标按钮缺 `aria-label`。
  - 修复：导航改 `<a>`/`<button>`，加 `aria-label`。

---

## 四、feature-dev 意图池（feature-dev 自动消费，勿手动改结构）

### 进行中（doing）
（当前无）

### 待办（open）
- **P2 · ApiKeys 页真实化**
  - 现状：纯 mock + Alert 说明，后端当前无 ApiKey REST 端点（见 B8）。
  - 目标：后端端点就绪后，接真实 CRUD + 复制密钥交互。
  - 状态：open（阻塞：需后端端点）
- **P3 · Conversations 页加搜索 / 状态筛选**
  - 目标：复用现有 conversations 数据，加关键词搜索与状态筛选，交互与 Agents 页一致（见 B5 一并规划详情页）。
  - 状态：open

### 已完成（done）
- **P1 · 补全 Agents 页「新建 Agent」死按钮** — 已接 `POST /api/v1/agents`：`api.ts` 加 `createAgent`（camelCase 字段），`AgentsPage` 加 Modal 表单（名称 / 角色 / System Prompt），提交后刷新列表；`e2e` 加「点击打开对话框」冒烟。QA 五道闸门全绿。

---

## 五、竞品对标新增功能意图（来自 `./competitive-roadmap.md`）

> 以下为对比 Dify / n8n / LangGraph / Coze / Flowise 后识别的**平台级新增功能**。多数涉及后端新增模型/端点 + 前端画布重构，属高风险改动——feature-dev 实施到接口契约/鉴权/路由层级会**停下问人**，不自动改。优先级与路线图分期一致。

### 已完成（done）
- **P0 · 后端工作流更新端点 + 前端编辑链路修通**
  - 目标：后端加 `PUT /api/v1/workflows/{id}`（草稿更新/元数据改）；前端 `/edit` 改调 PUT 带 id、拆分「保存草稿 / 运行」；SSE 改 `fetch`+`ReadableStream` 带 JWT；`JSON.parse(context)` 加 try/catch。
  - 关联：backlog B1/B2/B3/B4；**设计文档 `./put-workflow-design.md`（端点契约 / 聚合变更 / 命令处理器 / 前端配套 / 验收清单 / 待拍板决策 §7）**。状态：done（2026-07-22 已实现并过质量门；见 `docs/quality/p0-workflow-update-gate.md`）

### 待办（open）
- **P1 · 可视化 DAG 画布 MVP**
  - 目标：后端引入 `WorkflowNode`/`WorkflowEdge` + `StepType` 枚举 + 拓扑序执行；前端画布（拖拽/连线/缩放/小地图/撤销重做）+ 配置侧栏 + 基础节点 Start/End/LLM/Agent/Critic + 单步试运行 + 变量监视。
  - 风险：需后端 DAG 模型，前端从表单式升级为画布。状态：open（high-risk）
- **P2 · 工作流版本管理 + 导入导出**
  - 目标：Workflow 版本快照 + 回滚 + JSON 导入导出（对齐 Coze/Dify）。状态：open
- **P2 · 节点全家桶**
  - 目标：Code(JS/Py)、HTTP、Tool（接 `ToolDefinition`）、Knowledge Retrieval（显式节点：选库+topK+检索方式+重排）、Condition/If-Else、Loop/Iteration、Variable/聚合、Sub-workflow、Delay、User-Input(HITL 审批门)。状态：open（high-risk）
- **P2 · 触发器（Webhook / 定时 / Chat）**
  - 目标：工作流可被 Webhook / cron / 对话触发（对齐 n8n/Coze）。状态：open
- **P3 · 发布为 API / MCP Server**
  - 目标：每工作流生成可调 REST 端点（复用现有 API Key）；探索「发布为 MCP Server」（后端已有 MCP `ToolDefinition` 基础）。状态：open（high-risk）
- **P3 · 模板市场 / 示例库**
  - 目标：内置 5–10 个行业模板一键克隆。状态：open
- **P3 · 执行 Trace / 评估视图**
  - 目标：节点级耗时/token/IO 的 trace 视图 + 数据集回归评估（对标 LangSmith/Langfuse）。状态：open
- **P2 · 工作流调试器（变量监视 + 单步重跑 + 错误分支）**
  - 目标：Dify 式变量监视面板（实时查看节点输入/输出/中间变量）+ 节点级重跑（不重跑上游）+ 错误分支 / 「忽略异常」默认输出。状态：open（关联 competitive-roadmap §3 C / §4 P2）
- **P3 · 企业增强（多工作空间 / 用量仪表盘 / 工作流 diff）**
  - 目标：多工作空间隔离与切换、平台用量统计仪表盘（调用量/token/成本）、Git 式工作流 diff 视图。状态：open（关联 competitive-roadmap §4 P3）

### 差异化优势（保留，勿稀释）
- **Negotiation 协商式多智能体 + Critic 收敛**：Dify/n8n 无此原生原语。画布应提供「Agent-Team / Negotiation」专属模式（多 Agent 节点 + Critic + 收敛终止条件）。状态：native（后端已具备，待产品化）
