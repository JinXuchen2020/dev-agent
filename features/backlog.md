# 功能待办池（backlog）

> **位置与定位**：仓库根 `features/backlog.md`（与 `src` 平级）。本池同时收纳**前端与后端**的 feature 设计与实现意图；**新增 feature 须先将设计文档放入 `features/`，再进入实现**（见下方红线）。
>
> **两层级消费模型**：
> - **Tier 1 · Feature 史诗（F1–F8）** —— 每个 = 一份 `features/<id>.md` 设计文档 + 端到端实现，是 `feature-builder` 的取数单元（取最靠前 `open` 史诗）。
> - **Tier 2 · 验收子项** —— 原 B/O/P 条目的归并，挂在对应史诗下，既作该史诗的完成判据，也可被 `feature-dev` 单独取出做细粒度前端实现。
>
> **红线**：代理**绝不**自己发明需求。只实现这里列出的、或你当轮明确指令的功能。涉及接口契约 / 鉴权 / 路由结构 / 破坏性后端等高风险改动时，代理会停下问人，不自动改。

状态图例：`open`(待做) · `doing`(进行中) · `done`(已完成) · `blocked`(阻塞等待依赖)

代码基现状（2026-07-22 全量走查）：React 19 + Vite 8 + TS（strict）+ Antd 5 + @xyflow/react + zustand；`typecheck/lint/build/unit/e2e` 五道闸门当前全绿。优点：严格 TS、0 处 `any`、0 TODO/FIXME、lint 净。问题集中于「前端数据真实性 / 鉴权态 / 错误兜底 / 工程化」与「后端行动层（工具·代码·调研）空心」两类。

---

## Feature 史诗（Tier 1 —— feature-builder 消费单元）

> 每个史诗含：目标 + 验收子项（原 B/O/P 归并，保留 `文件:行号` 锚点）+ 优先级 + 风险 + 设计文档链接。验收子项里的前端细项可由 `feature-dev` 直接取做。

### F1 · 前端数据真实性 & 全局错误态  [P2]  done  （已于 2026-07-23 经 feature-builder 流水线端到端实现并提交 `96fa5ee`；设计文档 features/frontend-data-truth.md + 质量报告 docs/quality/frontend-data-truth-gate.md）
- 设计文档：`features/frontend-data-truth.md`（已建）
- 目标：UI 展示真实登录身份、失败有兜底，消灭静默吞错与无 404 白屏。纯前端、低风险，无后端契约变更。
- 验收子项：
  - **B7** Dashboard 假数据 —— ✅ **done（漂移校正）**：现走真实 `getAgents/getWorkflows/getExecutionLogs`，原行号已失效。
  - **B8 / ApiKeys 页真实化** —— 🔒 **blocked**：后端无公开 API Key 端点（GET/POST/DELETE /api-keys、复制密钥），属 Phase 6 后端范围；待端点就绪后独立 feature 实现，本次不做。
  - **O4** 真实用户身份上顶栏（从 JWT 解码 email/role；dev-login 令牌无 tenant_id 声明，故不展示 tenant）→ open（本次实现）。
  - **O5** 静默吞错 → 统一错误 Alert + 重试（受改：`DashboardPage`、`ExecutionLogDetailPage:50`）→ open（本次实现；复用 `useApiState` + `ErrorState`）。
  - **O1** 顶层 ErrorBoundary —— ✅ **done（漂移校正）**：`components/ErrorBoundary.tsx` 已在 `App.tsx:32` 挂载包裹全部路由。
  - **O11** 404 兜底（新增 `NotFoundPage` + `App.tsx` `*` catch-all）→ open（本次实现）。文档链接原引用已失效（前端无硬编码文档链接），聚焦 404。

### F2 · 登录与鉴权态一致性  [P1]  done  ✅（2026-07-23，分支 `feat/f2-login-auth-state`，commit 19af124）
- 设计文档：`features/auth-ux.md`（已建）
- 目标：登录凭证真实校验、401 不破坏 SPA、鉴权态前后一致。
- 验收子项（全勾）：
  - **B6** 真实密码校验（PBKDF2）+ `LoginPage` 密码框 → ✅ 已实现。
  - **O2** 401 派发 `auth:unauthorized` 事件 → App 路由层 `<Navigate>` 跳转 `/login`，无整页刷新 → ✅。
  - **O3** demo 会话隔离（`isDemo` 跳过 401 跳，本地占位身份）→ ✅。
  - **O8** httpOnly + SameSite Cookie 鉴权（去 localStorage + 顺带解 B2 SSE 鉴权）→ ✅。
  - **O8 衍生** CORS 去 AllowAnyOrigin 改 WithOrigins+AllowCredentials；`/auth/me` 替代前端 JWT 解码 → ✅。
- 三道质量门禁全 PASS（ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer）；质量报告 `docs/quality/f2-auth-gate.md`。
- 已知残留（非阻断）：多租户登录（P2 waiver，目标后续 feature）；`JwtSecretKey`/`AesEncryptionKey` dev 兜底值（生产须环境变量覆盖）；种子默认密码生产须改。

### F3 · 页面交互打磨  [P2]  done  （2026-07-24，分支 feat/f3-page-polish，设计文档 features/page-polish.md，质量报告 docs/quality/f3-page-polish-gate.md；纯前端，无后端契约变更）
- 设计文档：`features/page-polish.md`（已建）
- 目标：列表/筛选/表单交互正确且一致。
- 验收子项：
  - **B9** AgentConfigurations 的 YAML 从不展示（`AgentConfigurationsPage.tsx:11-17`）→ 详情抽屉展示 `yamlContent`（语法高亮可选）。
  - **B10** 状态筛选枚举可能大小写不匹配后端（`ExecutionLogsPage.tsx:56-61`）→ 前端建状态映射表，不裸传字面量。
  - **B11** Workflows「快速运行」无错误处理且可能建空工作流（`WorkflowsPage.tsx:27-33`）→ try/catch + 失败 toast + 空名 `message.warning`。
  - **Conversations 搜索/状态筛选**（复用 conversations 数据，交互对齐 Agents 页）→ open。
  - **O12** 列表分页与后端 `totalCount` 不一致（`ExecutionLogsPage.tsx:70-77`）→ 接入服务端分页（传 `skip/take` + 用 `totalCount`）。
  - **O13** 无请求取消（AbortController）/ 卸载后 setState 风险 → effect cleanup + `AbortController`。

### F4 · 前端工程化（性能/可维护性/可访问性）  [P2/P3]  done  （2026-07-24，分支 feat/f4-frontend-engineering，设计文档 features/frontend-engineering.md；质量报告 docs/quality/f4-frontend-engineering-gate.md）
- 设计文档：`features/frontend-engineering.md`（已建）
- 目标：拆包、去告警、清死代码、补单测、a11y。
- 验收子项：
  - **O6** 打包体积过大未拆包（单 chunk 1.38MB；`@xyflow/react`、antd 全进主包）→ 路由级 `React.lazy` + `manualChunks`。
  - **O9** antd 静态 `message` 警告/上下文丢失（LoginPage/AgentsPage 等）→ 改 `App.useApp()`。
  - **O10** 死代码/未用能力；编辑器节点不可编辑/删除（`appStore.ts:7-8,20,24`；`AppLayout.tsx:178-210`；`WorkflowEditorPage.tsx:78-88`）→ 删或补交互。
  - **O14** 可访问性薄弱（导航 `<div onClick>`、静态搜索框、缺 `aria-label`）→ 改 `<a>`/`<button>` + `aria-label`。
  - **O7** 单元测试覆盖极低（<5%）→ 补关键页单测（数据获取/错误态/表单/SSE 解析）。

### F5 · 行动层落地（Agent 真正能做事）  [P0]  done  ✅（2026-07-24，分支 feat/f5-action-layer，设计文档 features/action-layer.md，质量报告 docs/quality/f5-action-layer-gate.md；范围已确认 A1+A2(进程沙箱)+A3 一并纳入）  🔴高风险（Phase 6 行动层）
- 设计文档：`features/action-layer.md`（已建）
- 目标：让 Agent 真正在外部世界执行动作——调工具、跑代码、检索外部知识，而非伪造成功。这是「agent 编排平台」成立的核心。
- 验收子项：
  - **A1** 工具调用执行层全空心（三个 `IToolExecutor`：`NativeToolExecutor`/`SkillPackageExecutor`/`McpClient` 均直接返回伪造成功）→ ✅ done（`NativeToolExecutor` 接真实 HTTP 执行，单测走真实 `SendAsync` 路径覆盖成功/失败/超时/方法解析；Skill/MCP 执行器保留为 Phase 6 占位，设计文档明确 A1 仅要求 NativeToolExecutor 真实化）。
  - **A2** 代码沙箱为桩（`DockerCodeSandbox.cs:9-56`）→ ✅ done（进程级真实执行：`ProcessCodeSandbox` 用 `System.Diagnostics.Process` 拉起 python/node 真实运行并回传 stdout/stderr/ExitCode/超时杀进程；原 `DockerCodeSandbox` 桩改为显式抛异常消除静默假成功；真实 Docker 容器执行因本沙箱无 Docker 守护进程 + 未引 Docker SDK，列入 Phase 6）。
  - **节点全家桶·Tool/Code/Knowledge Retrieval** → ✅ done（新增 `ToolStepExecutor`/`CodeStepExecutor` 注册为 `StepType.Tool=6`/`Code=7`，经既有 `ResolveExecutor`(`HandlesType`) 真实路由；前端 DAG 画布补 Tool/Code 节点调色板/图标/配置面板；Knowledge Retrieval 已于 RAG 地基层完成）。
  - **已知残留（非阻断，已拆为独立 feature F9–F12）**：① 真实 Docker 容器隔离；② Skill/MCP 执行器占位（设计文档明确 A1 仅要求 NativeToolExecutor 真实化）；③ 进程模式 OS 层禁网不可强求（以 `NetworkEnabled=false`+语言白名单+超时杀+输出截断缓解）；④ 含 Tool/Code 节点的全链路 e2e 需后端+Web 实例，本沙箱未跑（单元层已覆盖真实执行路径）。

### F6 · Research Agent（联网多步调研）  [P1]  done  （2026-07-24，分支 feat/f6-research-agent，设计文档 features/research-agent.md；范围已确认：SerpApi + ResearchPage + SSE 流式 + 全认证用户）  ✅高风险已收口
- 设计文档：`features/research-agent.md`（已建）
- 目标：SK 集成 SerpAPI（或等价搜索 API），实现「开放问题 → 多步搜索 → 结构化报告」的调研 Agent；多步链真实串联、外部 API 真实调用（非伪造结果）。
- 风险：外部 API 密钥与限流；多步链上下文膨胀须走统一预算压缩（复用 `ITokenCounter`/`StringHelpers.Truncate`，对齐蓝图附录 C）。
- 验收：多步搜索 → 结构化报告且外部搜索 API 真实 HTTP 调用（mock transport 覆盖真实请求路径）。
- 关键设计：独立 `ResearchCommandHandler`（非工作流 step 循环）；`ISearchProvider`+`SerpApiSearchProvider`（真实 HttpClient，密钥走 `SearchSettings`/环境变量，**不落库**）；复用 `IModelClient`（测试可 StubModelClient）。

### F7 · 工作流平台化（program，可拆子史诗）  [P2/P3]  open  ⚠️高风险
- 设计文档：`features/workflow-platformization.md`（待建，含子史诗拆分；来源 `./competitive-roadmap.md` 对标 Dify/n8n/LangGraph/Coze/Flowise）
- 目标：把 DAG 画布 MVP 推向生产级平台能力。以下子项各自可独立成 `feature-builder` 任务：
  - 工作流版本管理 + 导入导出（快照/回滚/JSON）。
  - 节点全家桶：Code(JS/Py) / HTTP / Tool(接 `ToolDefinition`) / Knowledge Retrieval(选库+topK+重排) / Condition / Loop / Variable / Sub-workflow / Delay / User-Input(HITL 审批门)。
  - 触发器：Webhook / 定时(cron) / Chat。
  - 发布为 API / MCP Server（复用现有 API Key；后端已有 MCP `ToolDefinition` 基础）。
  - 模板市场 / 示例库（5–10 行业模板一键克隆）。
  - 执行 Trace / 评估视图（节点级耗时/token/IO；数据集回归，对标 LangSmith/Langfuse）。
  - 工作流调试器（变量监视 + 单步重跑 + 错误分支）。
  - 企业增强：多工作空间隔离与切换 / 用量仪表盘 / 工作流 diff。

### F8 · 差异化优势产品化（Negotiation + Critic）  [native]  open
- 设计文档：`features/negotiation-productization.md`（待建）
- 目标：后端已具备 Negotiation 协商式多智能体 + Critic 收敛原语，待产品化画布「Agent-Team / Negotiation」专属模式（多 Agent 节点 + Critic + 收敛终止条件）。
- 说明：**保留，勿稀释**——Dify/n8n 无此原生原语，是本平台差异化壁垒。

### F9 · 代码沙箱容器隔离（DockerCodeSandbox 真实化）  [P2]  open  ⚠️中风险（Phase 6 行动层 / 新增 Docker SDK 依赖）
- 设计文档：`features/sandbox-docker.md`（待建）
- 来源：F5 残留 ①（A2 进程沙箱已真实化；`src/AgentPlatform.Infrastructure/Sandbox/DockerCodeSandbox.cs` 现为显式抛异常占位，需补真实容器执行）。
- 目标：在有 Docker 守护进程的环境，用 `Docker.DotNet` 真实拉起隔离容器执行用户代码，提供比进程沙箱更强的文件系统 / 网络 / 资源边界。
- 验收子项：
  - 引入 `Docker.DotNet` 依赖；`DockerCodeSandbox` 由抛异常改为真实 `RunCodeAsync`/`RunCommandAsync`：镜像拉取/创建、挂载代码文件、容器内运行、捕获 stdout/stderr/ExitCode、资源限制（cpu/mem）、超时 kill、输出截断至 `SandboxSettings.MaxOutputBytes`。
  - `Sandbox:Provider=Docker` 时经 DI 条件注册切到真实 `DockerCodeSandbox`（F5 已留条件注册位 `DependencyInjection.cs`）。
  - 真实副作用单测：需在提供 Docker 守护进程的 runner 上跑（本开发沙箱无 Docker，该 feature 门禁须在含 Docker 的 CI 跑，或提供可跳过集成测试标记）。
  - 默认 `Provider=Process` 不变，保证无 Docker 环境仍可运行。

### F10 · A1 残余执行器真实化（Skill + MCP）  [P2]  open  ⚠️中风险（Phase 6 行动层）
- 设计文档：`features/executor-realization.md`（待建）
- 来源：F5 残留 ②（F5 仅真实化 `NativeToolExecutor`；`src/AgentPlatform.Infrastructure/Tools/SkillPackageExecutor.cs` 与 `McpClient.cs` 保留 `// TODO(Phase6)` 占位，仍伪造成功）。
- 目标：让 SK 技能包与 MCP 工具真正执行，补全 Agent 三类动作源（Native / Skill / MCP）的真实副作用。
- 验收子项：
  - **Skill**：`SkillPackageExecutor` 接 SK runtime（Semantic Kernel plugin 加载 / 技能包运行器），按 `ToolDefinition.SkillPluginName` 真实调用插件函数，回真实结果与失败；契约不变（`IToolExecutor`/`ToolExecutionResult`）。
  - **MCP**：`McpClient` 接 MCP client（SSE / stdio transport），连接外部 MCP server，按 `ToolDefinition` 列出/调用工具，回真实结果；含连接失败/超时精准回打。
  - 单测：各自真实执行路径（SK 用内存插件或 mock runtime；MCP 用本地 mock MCP server / test transport）覆盖成功/失败。
  - 两者可独立排期（F7「发布为 MCP Server」复用此能力）。

### F11 · 沙箱 OS 级隔离增强（进程沙箱禁网/资源限额）  [P2]  open  ⚠️高风险（OS 级隔离，跨平台）
- 设计文档：`features/sandbox-os-isolation.md`（待建）
- 来源：F5 残留 ③（`SandboxSettings.NetworkEnabled=false` 在进程沙箱仅为声明，未在 OS 层强制；语言白名单 + 超时杀 + 输出截断为缓解项）。
- 目标：让 `Process` 沙箱在不引入 Docker 的前提下获得 OS 级网络隔离与资源约束，使 `NetworkEnabled=false` 真正生效。
- 验收子项：
  - Linux：`unshare`/`clone` 网络命名空间（或 `NetworkEnabled=false` 时禁网）+ cgroups v2 资源限额（cpu/mem/pids）+ 可选 seccomp 系统调用过滤。
  - macOS：`sandbox-exec` 配置（禁网 / 限文件访问）。
  - Windows：`AppContainer` / 作业对象（Job Object）资源限额。
  - 与 `SandboxSettings`（NetworkEnabled / TimeoutSeconds / AllowedLanguages / MaxOutputBytes）联动；跨平台抽象，失败安全（不支持平台回退现有缓解项并告警）。
  - 单测：在 CI 对应平台断言禁网生效（e.g. 代码尝试 socket 连接 → 失败）。

### F12 · Tool/Code 节点全链路 e2e  [P3]  open  🟢低风险（测试基础设施）
- 设计文档：`features/tool-code-e2e.md`（待建）
- 来源：F5 残留 ④（单元层已覆盖真实执行路径；含 Tool/Code 节点的端到端需后端+Web 实例，本开发沙箱未跑）。
- 目标：起真实后端 + Web 实例，跑一条含 Tool 节点（真实 HTTP）与 Code 节点（真实 python/node 子进程）的工作流，断言端到端 stdout/响应回填与节点状态。
- 验收子项：
  - 新建/扩展集成测试：用 `WebApplicationFactory` 起后端 + 本地 Mock HTTP 端点，构造含 `StepType.Tool`/`StepType.Code` 的 `WorkflowNode`，经 `WorkflowOrchestrator` 跑全流程，断言 `StepExecutionResult.Outcome` 与 `Output`。
  - 前端联动（可选）：用 Playwright/Cypress 在 Web 实例上拖出 Tool/Code 节点、配置、运行、断言画布节点状态与输出面板。
  - 纳入 CI e2e 阶段；本沙箱无 Docker 仍可跑（python/node 子进程 + 本地 HTTP 端点均可用）。

---

## 已完成归档（done）

> 已交付、留作追溯；不再进入排期。新完成项请从上方史诗移入此处。

### 功能缺陷 / 后端（原一~二节 done）
- **B1** 工作流编辑器「编辑模式」失效且保存即运行 → done（2026-07-22，`PUT /api/v1/workflows/{id}` + 拆分「保存草稿/运行」；`docs/quality/p0-workflow-update-gate.md`）
- **B2** 实时进度 SSE 鉴权 → done（F2 起 SSE 改用 cookie：`fetch(..., {credentials:'include'})` / `new EventSource(url, {withCredentials:true})`，不再手动塞 Bearer；`p0-workflow-update-gate.md` + `docs/quality/f2-auth-gate.md`）
- **B3** WorkflowDetail SSE 无限重连刷屏 → done（AbortController.abort + 非 2xx 返回；`p0-workflow-update-gate.md`）
- **B4** WorkflowDetail 解析 context 白屏 → done（try/catch 回退原始文本；`p0-workflow-update-gate.md`）
- **B5** 会话功能不可用 → done（2026-07-23 聊天页打通 + KB 联动；`docs/quality/conversation-kb-linkage-gate.md`；发消息 RBAC 放开为「所有已认证租户用户」）
- **R1** 知识库无入库通道（RAG 静默 no-op）→ done（2026-07-23 地基层；`docs/quality/rag-foundation-gate.md`）
- **R2** 向量检索无租户隔离 → done（`document_embeddings.tenant_id` + WHERE；`rag-foundation-gate.md`）
- **R3** RAG 部署强耦合 SQLite 触发 500 → done（条件注册 + InMemory 回退；`rag-foundation-gate.md`）
- **R4** 检索无相关性阈值 + 工作流 RAG 语义错位 → done（minScore；`rag-foundation-gate.md`）

### 竞品对标（原五节 done）
- **P0** 后端工作流更新端点 + 前端编辑链路修通 → done（2026-07-22；`docs/quality/p0-workflow-update-gate.md`；设计 `./put-workflow-design.md`）
- **P1** 可视化 DAG 画布 MVP → done（2026-07-23；`docs/quality/p1-dag-workflow-gate.md`；设计 `./dag-workflow-design.md`）

### feature-dev 意图池 done
- **P1** 补全 Agents 页「新建 Agent」死按钮 → done（`POST /api/v1/agents` + Modal 表单；QA 五道闸门全绿）
