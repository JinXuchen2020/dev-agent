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

> **F13（多租户凭据配置）已 done**；其 UX 衍生「填 Key+Base URL 后拉取可访问模型清单」已拆为独立最高优先级史诗 **F14（供应商模型发现 [P0]）**，见下方，设计文档 `./model-discovery.md` 已建。F13 实现中 S3（单条 upsert→多条目列表）、S4（并入配置页→独立「我的凭据」页）两项决策已被用户实战推翻并反转，最新状态以 `./model-config.md` 与 `./model-discovery.md` 为准。

### F13 · 多租户凭据配置（模型 + 搜索，BYO-Key + 平台内置）  [P0 最高优先级]  done  🔴高风险（破坏性后端 + 密钥安全 + 多租户隔离）
- 设计文档：`features/model-config.md`（已升级为「通用租户凭据」，Model + Search 两 `Category`）
- 目标：补齐多租户化最后一环——外部 API 凭据层租户隔离（**模型 LLM key + Research 用 SerpApi 搜索 key 同构处理**）。双轨：A 用户自配凭据（模型 provider/API Key/BaseUrl/模型名，或搜索 SerpApi Key，按租户隔离）；B 平台内置凭据（运营方 `appsettings` 已配密钥暴露为可选，替代哑 stub / 全局 `SearchSettings` 硬编码，作上手/试用层）。
- 核心改造：把 `SemanticKernelModelClient`（现全局启动时构建）与 `SerpApiSearchProvider`（现构造时固化 `SearchSettings.SerpApiKey`，见 `SerpApiSearchProvider.cs:26,32,44`）都改为 per-tenant 解析；抽 `ITenantCredentialResolver`（category=Model/Search）+ `TenantProvider`；`ModelRouter`/`SerpApiSearchProvider` 运行时按租户取 key，无则回退平台默认。新增 `TenantCredentialSetting` 聚合（落库加密复用 `IApiKeyEncryptionService` AES-256-GCM，掩码 `••••`+prefix）；端点 `GET/PUT /api/v1/tenant/credentials?category=Model|Search`（RBAC Admin/Operator）+ `GET /api/v1/models`（平台模型清单）；前端 `CredentialSettingsPage`（`Tabs: 模型 + 搜索`）+ 模型下拉接平台模型。配额经扩展 `ICostController` 为租户键控（`PerTenantDailyBudget` 模型 / `PerTenantDailySearchQuota` 搜索，防 B 滥用）。
- 验收子项（核心）：
  - **A 模型租户隔离**：租户 A 的 key/请求绝不泄漏到 B；`HasQueryFilter` 生效；`SendMessage` 实际走 A 的 key（StubHttpMessageHandler 验证请求构造）。
  - **A 搜索租户隔离（本次重点）**：租户 A 配 SerpApi `aaa`、B 配 `bbb`；`SerpApiSearchProvider.SearchAsync` 在 A 上下文发 `api_key=aaa`、B 上下文 `api_key=bbb`（StubHttpMessageHandler 捕获 URL 断言）；B 取不到 A 的 key；Research 跑通「plan → 用租户 key 真实检索 → synthesize」。
  - **密钥安全（P0）**：明文 key 不落库/不出 API/不进日志（模型 + 搜索同标）；`GET` 返回掩码；`TenantCredentialSetting.Id` 必须 `ValueGeneratedNever()`（规避 EF 并发陷阱）。
  - **B 平台凭据**：运营方配模型 key 时 `GET /api/v1/models` 返回该模型；无 BYO-Key 租户走平台模型得真实回复；运营方配 `Search__SerpApiKey` 时，无 BYO-SerpApi 的租户 Research 仍能真实联网检索（平台内置搜索）。
  - **降级链**：无配置且无平台 key → 模型 stub 占位提示、搜索 `Success=false` 明确提示配 key；配了即生效（缓存失效正确）。
  - **配额**：平台模型超 `PerTenantDailyBudget` / 平台搜索超 `PerTenantDailySearchQuota` → 拒绝并提示配 BYO-Key；BYO-Key 不受限。
  - **QA**：build 0/0、`dotnet test` 全绿（含新增租户隔离/加密/路由合并/搜索租户化单测）、前端 `tsc`+`qa.mjs` 全绿、既有 238 测试不回归。
- 决策状态（见 `./model-config.md` §7，2026-07-27 全部锁定）：S1 仅 OpenAI 兼容 / S2 B 启用+默认配额（模型 `PerTenantDailyBudget=1.00` USD/天、搜索 `PerTenantDailySearchQuota=100` 次/天）/ S3 每租户每类单条 upsert / S4 并入 Agent 配置页（不新增独立 `CredentialSettingsPage`）/ S5 搜索纳入（SerpApi key 前台可配）/ S6 搜索仅 SerpApi。
- **完成记录（2026-07-27）**：feature-builder 全栈实跑落地。后端 Domain(`TenantCredentialSetting`+`CredentialCategory`+仓储)/Application(`ITenantCredentialResolver`/`ITenantModelClientResolver`/`IPlatformModelProvider`+租户键控 `ICostController`+`ModelRouter` 新 ctor)/Infrastructure(`SemanticKernelModelClient.CreateForTenant` 工厂/`TenantCredentialResolver`+缓存失效/`TenantModelClientResolver`/`PlatformModelsProvider`/`SerpApiSearchProvider` 运行时按租户解析 key/`CostController` 租户键控/EF 迁移 `AddTenantCredentialSetting`)/Api(`TenantCredentialsController`+`PlatformModelsController`+DTOs)。前端 Agent 配置页内嵌 `Tabs: 模型+搜索` 凭据配置（`Input.Password` 掩码 + provider Select + 保存），`types/index.ts`+`api.ts` 补齐；`tsc --noEmit` 0 error。三道质量门 PASS（`.quality-gate.json` 推进 `f13-multi-tenant-credentials`）；`dotnet test src/AgentPlatform.sln` 全绿（含 F13 新增 EF 集成测试验证落库+租户隔离+upsert 不重复行）。**审查修复 P0**：`TenantCredentialsController.Put` 原直接写仓储但未提交 `IUnitOfWork.SaveChangesAsync`（本控制器不走 MediatR 命令、无 `UnitOfWorkBehavior` 自动提交），导致凭据永不落库——已注入 `IUnitOfWork` 显式提交，与命令处理器行为一致。

### F14 · 供应商模型发现（填 Key+Base URL 后拉取可访问模型清单）  [P0 最高优先级]  open  🔴高风险（新增端点 + 鉴权 + 路由 + 前端契约）
- 设计文档：`features/model-discovery.md`（已建，§6 决策 D1 已锁定）
- 目标：用户在「我的凭据」/Agent 配置页填 API Key + Base URL 后，可一键拉取该 provider 账户下所有可访问模型（`GET /v1/models`，OpenAI 兼容），以下拉供选择，免去手动拼模型名。来源 F13 凭据配置 UX 衍生（F13 当前 Model Name 为手动文本框）。
- 核心改造：
  - 后端新端点 `POST /api/v1/tenant/credentials/discover-models`，body `{ provider, apiKey, baseUrl? }`；解析 provider 默认 BaseUrl（OpenAI→`https://api.openai.com/v1`、DeepSeek→`https://api.deepseek.com/v1`，VLLM/Custom 必填 baseUrl）/ `IHttpClientFactory` `GET {baseUrl}/models`（`Bearer`，请求级超时 15s）/ 解析 OpenAI 兼容 `{data:[{id,owned_by}]}` 返回 `List<ProviderModelInfo>(Id,OwnedBy)`；错误 401/403/404/超时/传输 → 400 + 中文原因；`[Authorize(Roles="Admin,Operator")]`；密钥仅探测，不落库不记日志。
  - 新服务 `IProviderModelDiscovery` + `ProviderModelDiscovery`（Infrastructure），DI `AddScoped`；单测 mock `HttpMessageHandler` 覆盖 URL/解析/401。
  - 前端 `CredentialForm`：模型类 Model Name 由 `Input` 改 `AutoComplete`（选项来自发现结果，允许手动自定义）；「拉取模型」按钮（填 Key+BaseUrl 可用，loading+错误）；edit 模式 Key 留空时按钮禁用并要求先填 Key（**D1：不做后端解密存量密钥探测**）。
- 验收子项：
  - 后端 discover-models：OpenAI/DeepSeek 默认 base 补全正确；VLLM/Custom 缺 baseUrl → 400；401/403/404 → 400 中文原因；解析 `id` 正确；空 `data` → 200 空数组；`StubHttpMessageHandler` 单测覆盖。
  - 前端 AutoComplete 下拉填充 + 允许自定义；按钮 loading/错误；edit 模式留空 Key 时按钮提示先填 Key。
  - e2e（Python UTF-8）：登录 → 填 Key+BaseUrl → discover → 返回模型列表 → 选一个 → 保存 → `GET /tenant/credentials` 列表含该 model。
  - 质量门：build 0/0、`dotnet test` 全绿（含 discovery 单测）、前端 tsc 0 + vitest 全过 + vite build；实现后追加 `.quality-gate.json` notes 并保 `cleared:true`。
- 决策（见 `./model-discovery.md` §6）：D1 edit 模式探测密钥 = 仅用表单现填 Key（不做后端解密存量，用户 2026-07-27 拍板）/ D2 范围仅模型类（搜索无模型列表语义）/ D3 无 schema 变更（不落库，无 EF 迁移）/ D4 安全边界（Admin 专用+用户自有 provider 出站，密钥不落库不记日志）。
- 风险：🔴 高风险（新增端点+鉴权+路由+前端契约，触发 feature-dev 高风险闸口，先设计后实现）；出站请求 SSRF 面（Admin 专用可接受，后续可加域名白名单，不在本范围）；非标准 `/models` 返回容错（缺 `owned_by`/非数组 data 容忍）。

### F15 · 多语言国际化（i18n，暂仅中文 + 英文）  [P1]  open  🟡中风险（前端跨切面文案抽取 + 全局 Provider 注入）
- 设计文档：`features/i18n.md`（已建，§5 决策 D1–D4 待锁定）
- 目标：引入 `i18next` + `react-i18next`，顶栏「中文 / English」切换并持久化到 localStorage（默认 zh-CN）；Antd `ConfigProvider` 与 `dayjs` 区域同步。v1 仅 zh-CN + en-US 两套，优先抽取 UI 框架级文案（导航/标题/按钮/表单标签/空态/错误态），领域数据（用户填的 agent 描述等）不做。
- 核心改造：
  - 新增 `src/locales/`（index.ts 初始化 + zh-CN.ts + en-US.ts + config.ts），`i18n.use(initReactI18next).init(...)`，`lng` 读 `localStorage('app-locale')`、回退 `zh-CN`。
  - 新增 `components/LanguageSwitcher.tsx`（顶栏右上角，切 `i18n.changeLanguage` + 持久化 + 触发 `ConfigProvider locale` / `dayjs.locale` 联动）；`App.tsx` 顶层 `ConfigProvider` 随 `languageChanged` 事件切 `antd/locale/zh_CN`↔`en_US`、`dayjs` 切 `zh-cn`↔`en`。
  - 各页面/组件 `useTranslation()` + `t('area.key')` 替换硬编码中文；v1 优先级：LanguageSwitcher+AppLayout 菜单+LoginPage → common 通用词 → 各页标题/Empty/message → CredentialForm/CredentialManager/ErrorState/useApiState。
  - 资源用嵌套对象、点分隔 key（如 `nav.agents`/`common.save`），动态文本用插值 `t('key',{name})`；**不触后端**（v1 后端错误原文仍按原样展示，见 D1）。
- 验收子项：
  - 基础设施：i18n 初始化、zh-CN/en-US 齐备、默认 zh-CN、刷新从 localStorage 恢复。
  - 切换器：顶栏可切中/英、即时生效、Antd+dayjs locale 同步；各页标题/按钮/表单标签/空态/错误态双语正确、默认中文视觉不回归。
  - 质量门：tsc 0 error + vitest 全过 + vite build 通过；新增资源 key 对称性单测（zh-CN 与 en-US 顶层 key 一致）；`.quality-gate.json` 追加 notes 保 cleared:true。
- 决策（见 `./i18n.md` §5，待锁定）：D1 后端错误文案 v1 不本地化 / D2 资源用 .ts 对象 / D3 默认 zh-CN / D4 v1 仅 UI 框架级文案（领域数据不做）。
- 风险：🟡 中风险（几乎全前端页文案抽取，工作量大；key 规范需统一）；缓解：先定 common/nav/login 高频命名空间，按 §3.5 优先级分批小步提交；第三方画布(@xyflow/react)内置菜单 v1 可能仍中文，列已知残留。

### F16 · 列表统一改为卡片（Card）形式展示  [P2]  open  🟡中风险（前端多列表页渲染层改造）
- 设计文档：`features/card-layout.md`（已建，§5 决策 D1–D4 待锁定）
- 目标：用户要求「所有页面的列表都用 card 形式展示」——把各实体列表页的 `<Table>` 替换为响应式卡片网格。新增通用 `components/EntityCardGrid.tsx`（网格 + 加载骨架 + 空态 + 响应式列），各页用 `renderCard(item)` 提供单卡（标题/摘要/状态 Tag/操作菜单），保留搜索/筛选栏与分页、删除 `Popconfirm`。
- 核心改造：
  - 目标页（v1 改卡片）：`AgentsPage`/`AgentConfigurationsPage`/`WorkflowsPage`/`ConversationsPage`/`KnowledgeBasesPage`/`CredentialManager`(凭据)/`ApiKeysPage`/`ExecutionLogsPage`/`AgentRolesPage`/`ResearchPage`（后者已是 `<List>`，可保持或适配网格）。⚠️ `AgentConfigurationsPage` 与 **F17** 强耦合（F17 会移除凭据 tab + 补 CRUD + 可能卡片化），建议 F16 先于 F17 或两者由 F17 统一收口该页（见 F17 D2）。⚠️ `AgentRolesPage` 与 **F19** 强耦合（F19 会重写该页：补 CRUD + 内建分区 + 引用计数），建议 F16 先于 F19 或两者由 F19 统一收口该页（见 F19 D1/风险）。
  - `EntityCardGrid<T>`:`{ items, renderCard, loading?, emptyText?, gutter?, onItemClick? }`；`loading`→`Skeleton` 卡、`empty`→`Empty`、默认 `Row gutter=[16,16]` + `Col xs=24 sm=12 md=8 lg=6`（大屏 4 列）。
  - 操作：卡片右上 `Dropdown`（⋯）收纳 编辑/删除/运行，删除保留 `Popconfirm`；点击卡进详情/开抽屉；分页（如日志的 `totalCount`）用网格下方 `Pagination` 复用现有 `skip/take`。
  - 与 F15 i18n 协同：卡片静态文案走 `t()`（D3 建议 F16 落地即采用，避免 F15 二次抽取）。
- 验收子项：
  - 通用组件：loading 骨架 / emptyText 空态 / 响应式列 / onItemClick 齐备。
  - 覆盖度：上述列表页均改卡片，信息等价（标题/摘要/状态/操作不丢）；搜索/筛选/分页保留生效。
  - 质量门：tsc 0 error + vitest 全过（含 `EntityCardGrid` 渲染/空态/响应式单测）+ vite build 通过；`.quality-gate.json` 追加 notes 保 cleared:true。
- 决策（见 `./card-layout.md` §5，待锁定）：D1 执行日志默认也改卡片（多列压为卡片元信息，或保留表格作例外）/ D2 详情内子表（step entries/文档列表/Steps）v1 保留 Table / D3 与 F15 顺序（建议 F16 即 `t()`）/ D4 卡片密度默认大屏 4 列、日志可降 3 列。
- 风险：🟡 中风险（几乎所有列表页渲染层，工作量大）；缓解：先落 `EntityCardGrid` 单一基件，再逐页小步替换（每页一提交），优先高频页；信息密度须保关键字段（状态/时间/owner）不丢；与 F15 时序耦合（D3 规避）。

### F17 · AgentConfiguration 实例化联动（方案 A 细化）  [P2]  open  🟡中风险（前端 CRUD 补全 + 1 新端点 + RBAC 收敛；不触 EF 迁移）
- 设计文档：`features/agent-config-instantiation.md`（已建，§5 决策 D1–D4 待锁定）
- 目标：把"版本化 YAML 定义库孤岛"`AgentConfiguration` 变为真正有用的「Agent 定义/模板库」——前端补完整 CRUD + `AgentsPage`「基于模板新建」从定义实例化 Agent + 消除与「我的凭据」页重复 tab 与 RBAC 不一致。来源：2026-07-27 对 `AgentConfigurationsPage` 的分析结论（方案 A）。
- 核心改造：
  - 后端新端点 `GET /api/v1/agent-configurations/{id}/template`（`[Authorize(Roles="Admin")]`，tenant-scoped）：解析 `YamlContent`→返回 `ConfigurationAgentTemplate(Name,RoleCode?,ModelProvider?,ModelName?,ModelApiUrl?,SystemPrompt,SourceVersion)`；新增 `Infrastructure/Yaml/AgentConfigurationYamlParser.cs`（复用已引 `YamlDotNet`），容错缺字段→null、非法 YAML→400 中文原因。后端 CRUD(POST/PUT/DELETE/GET) 已就绪，仅差前端 UI 与新 template 端点。
  - （v1 可选溯源）`CreateAgentCommand` 加 `Guid? ConfigurationId`，handler 写审计溯源，不强制改 `Agent` 聚合（无 EF 迁移）。
  - 前端 `AgentConfigurationsPage`：移除重复的「凭据设置」tab（凭据统一走「我的凭据」路由 `/credentials`）；补 新建/编辑/删除 模态（name/description/agentTypeCode Select 取 `getAgentRoles()`/yamlContent 代码框）；非 Admin 隐藏写按钮（GET 列表为 `[Authorize]` 任意登录可读，写才 Admin，RBAC 自然一致）。
  - 前端 `AgentsPage`：加「基于模板新建」入口——列定义(`getAgentConfigurations()`)→选其一→`getAgentConfigurationTemplate(id)` 预填创建表单→用户微调→`createAgent`。
  - `AppLayout`：Configurations 菜单项与后端 `[Authorize(Roles="Admin")]` 对齐（Admin-only 可见），消除非 Admin 见报错入口。
  - `api.ts`+`types/index.ts`：补 `getAgentConfigurationTemplate`/`createAgentConfiguration`/`updateAgentConfiguration`/`deleteAgentConfiguration` + 对应 Request/Template 类型。
- 验收子项：
  - 后端 template 端点：字段映射正确；缺字段→null 兜底；非法 YAML→400 中文；跨租户 id→404；非 Admin→403；parser/handler 单测覆盖。
  - 前端：定义可 CRUD（无凭据 tab）；「基于模板新建」预填正确且可改；Configurations 仅 Admin 可见；e2e(Admin 登录→建定义→基于模板建 Agent→字段一致→清理)。
  - 质量门：build 0/0、`dotnet test` 全绿（含 parser/handler 单测）、前端 tsc 0 + vitest 全过 + vite build；`.quality-gate.json` 追加 notes 保 cleared:true。
- 决策（见 `./agent-config-instantiation.md` §5，待锁定）：D1 实例化=前端预填为主、后端 `ConfigurationId` 仅溯源（v1 不强制改聚合/无迁移）/ D2 与 F16 时序（建议 F16 先行，F17 在其卡片 UI 叠加 CRUD+模板；否则 F17 自行卡片化并标记 F16 跳过该页）/ D3 YAML 编辑器 v1 用 TextArea（不引 Monaco）/ D4 模板字段映射约定（YAML 采纳 `AgentYamlModel` 结构，前端以 template 端点返回为准不自解析）。
- 风险：🟡 中风险（前端 CRUD 跨模态 + 新端点 + AppLayout RBAC）；缓解：后端 CRUD 已就绪仅差 UI、template 端点只读+解析无写副作用、YAML 解析单点服务端；与 F16 渲染冲突见 D2 明确单一 owner 页。

### F18 · Dashboard 图表充实（运行分析看板）  [P1]  open  🟡中风险（新增 analytics 端点 + 前端图表库 + 时间聚合）
- 设计文档：`features/dashboard-charts.md`（已建，§7 决策 D1–D4 待锁定）
- 目标：把当前仅 4 个计数卡的 Dashboard 升级为运行分析看板（KPI 卡 + 时间序列/分布图），对标 Dify/LangSmith/Flowise/n8n/Coze。来源：2026-07-27 用户要求"充实 dashboard 图表并分析可形成图表的内容 + 对标竞品"。
- 现状核验（真实代码）：当前 `DashboardPage.tsx:35-66` 仅 4 个 `<Statistic>` 卡；后端**无 analytics REST 端点**（仅有 OpenTelemetry `/metrics` Prometheus 抓取，非前端消费）。可聚合数据源已确认：`ExecutionLog`(WorkflowId/WorkflowName/TenantId/Status/StartedAt/Entries[].Duration)、`Conversation`(TenantId/TotalTokenUsage/CreatedAt/Status)、`Agent`/`Workflow`(Status/CreatedAt)。成本数据缺口：无模型单价表 → v1 只做 Token 图不做 $ 成本图。
- 竞品共性高价值图表：① 运行量趋势 ② 成功率 ③ 延迟 ④ Token 消耗 ⑤ 按 Agent/工作流拆分 ⑥ 对话量 ⑦ 时间范围选择器(7/14/30 天)。
- 核心改造：
  - 后端新端点 `GET /api/v1/analytics/summary?from=&to=`（`AnalyticsController`，沿用 Dashboard 现有可见性 `[Authorize]` 已认证可读，tenant-scoped via `ITenantProvider`）：单一 `DashboardSummaryDto` 一次返回全部图表数据（KPIs + ExecutionsByDay + TokenByDay + ConversationsByDay + LatencyByDay + TopWorkflows），避免 N 请求。Handler 取区间内原始行（租户过滤）**应用层按日桶聚合**（v1；留 SQL GROUP BY 下沉余地）。复用 `IExecutionLogRepository`/`IConversationRepository`。
  - 前端：图表库推荐 `@ant-design/plots`（antd 官方 G2 封装，视觉一致），备选 `recharts`；`DashboardPage` 加 7/14/30 天范围选择器 + 6 KPI 卡（扩 Active Agents/Workflows/执行总数/成功率/总 Token/平均延迟）+ 图表网格 C1 执行量趋势(面积堆叠)/C2 成功率(折线)/C3 平均延迟/C4 Token 消耗/C5 对话量/C6 热门工作流 Top8(横向柱状)；`api.ts` 加 `getDashboardSummary(from,to)`；标签用 `t()`（与 F15 协同）。
  - **无 EF 迁移**（纯查询端点）。
- 验收子项：端点结构正确 + 租户隔离单测；前端 7/14/30 切换同步刷新、空数据空态不崩；KPI 与现有 4 卡口径一致；日桶聚合单测（成功率=completed/total、Token 求和、TopWorkflows 截前 8）；tsc 0 + vitest + vite build 通过。
- 决策（见 `./dashboard-charts.md` §7，待锁定）：D1 v1 仅 Token 图不做 $ 成本图（缺单价表）/ D2 图表标签用 `t()`（与 F15 协同）/ D3 可见性沿用已认证可读（非仅 Admin）/ D4 图表库默认 `@ant-design/plots`(recharts 备选)。
- 风险：🟡 中风险（新端点 + 图表库包体 + 应用层时间聚合）；缓解：聚合在应用层、量大再下沉 SQL；recharts 备选降包体；与 F16 不冲突（Dashboard 非表格页）、与 F15 协同即可。

### F19 · Agent Roles 内建标记 + 页面补全 + 分类合并（统一角色目录，DB 为准）  [P1]  open  🟡中风险（角色分类值对象 + 聚合加列 + EF 迁移 + 新增 PUT 端点 + 前端页重写）
- 设计文档：`features/agent-roles-builtin.md`（已建，§5 决策 D1–D4 已随用户拍板锁定）
- 目标：① 修 bug——系统架构/产品经理等平台默认角色被错标"自定义"（前端 `BUILT_IN_ROLES` 硬编码 code 与 DB `AgentRoleDefinition` code 整套对不上 + 聚合无内建标记）；② 补全 `AgentRolesPage`（新建/编辑/删除 + 被引用计数，后端 CRUD 已就绪但缺 `PUT`）；③ 合并两套分裂分类（`AgentType` 硬编码值对象 vs `AgentRoleDefinition` DB 表）为**一套以数据库为准的统一角色目录**。来源：2026-07-27 用户对 `AgentRolesPage` 的分析反馈（"系统架构等应是内建"+"页面功能不全"），并明确"两条分类合并成一套统一角色目录，以数据库为准"。
- 核心改造：
  - `AgentRoleDefinition` 增 `IsBuiltIn`(bool,default false) + EF 迁移 `AddAgentRoleIsBuiltIn`；`DatabaseInitializer` 种子 7 个内建（architecture/development/testing/product/documentation/reviewer/requirement，`IsBuiltIn=true`）。既有 Agent 的 `Agent.Role.RoleCode`（如 development）与内建 code 一致 → 数据连续，无需迁移 Agent。
  - 统一目录：`AgentRoleDefinition` 表 = 唯一权威；`AgentType` 值对象**降为内建目录的类型化镜像**，`Predefined` code 改为与 DB 内建完全一致；新增**架构 parity 测试**断言两者 code 集合相等 → 强制"DB 为准"，杜绝再次漂移。`Agent.Role` 仍绑 `AgentType`（不改动 Agent 聚合、不新增 Agent 迁移，低风险）。
  - 内建判定：`AgentRoleSummary` 增 `IsBuiltIn`；前端删硬编码 `BUILT_IN_ROLES`、按 flag 分区。
  - 页面补全：后端新增 `PUT /api/v1/agent-roles/{roleCode}` + `UpdateAgentRoleDefinitionCommand/Handler`；前端加「新建/编辑/删除」(内建 code 锁、不可删)；列表增「被引用 Agent 数」列（`IAgentRoleDefinitionRepository.CountAgentsByRoleCodeAsync`）。删除拦截内建/被引用（409）。
  - `AgentsPage` 默认 `roleCode:'developer'` → 修正为真实内建 code（如 `development`）。
- 验收子项：
  - 内建角色在页面"内建"区正确显示，不再误标 Custom。
  - 新建/编辑/删除可用（Admin）；非 Admin 无写按钮（RBAC 对齐）。
  - 内建删除拦截、被 Agent 引用的自定义角色删除拦截（409 提示）。
  - 列表显示每角色被引用 Agent 数。
  - parity 测试通过（`AgentType.Predefined` code == DB 内建种子 code）。
  - 现有 Agent 角色不丢、路由（`RoleBasedSelectionStrategy` 只按 StepName 匹配）行为不变（回归测试）。
  - 质量门：build 0/0、`dotnet test` 全绿（含 parity + 引用计数单测）、前端 tsc 0 + vitest 全过 + vite build；`.quality-gate.json` 追加 notes 保 cleared:true。
- 决策（见 `./agent-roles-builtin.md` §5，已锁定）：D1 合并策略=DB 为准 + AgentType 降镜像 + parity 测试（不动 Agent 聚合）；D2 内建集合 7 个（含新增 reviewer、沿用 requirement）；D3 补 PUT、内建 code 锁不可删；D4 AgentConfiguration.AgentTypeCode v1 不强制、仅文档建议。
- 风险：🟡 中风险（聚合加列 + 迁移 + 新端点 + 前端重写）；缓解：EF 铁律（迁移 + `#pragma warning disable IDE0161`）；`AgentType.Predefined` code 改动需同步改 SpecFlow `AgentTypeMigration`（`"architect"`→`"architecture"` 等）；与 F16 强耦合（见下方 F16 协同）。

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
