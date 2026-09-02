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

> **测试约定（2026-08-04 确立，feature-builder 硬约束 #7）**：**前端 E2E 必须 BDD 驱动**——凡触及 UI 的 feature，须配套 `playwright-bdd` 风格的 Gherkin E2E（`src/AgentPlatform.Web/e2e/features/*.feature` + `e2e/steps/*.steps.ts`，`createBdd(test)` 的 `test` 须 `extend` 自 `playwright-bdd` 自带 `test`），运行链路 `bddgen && playwright test`。禁止写裸 `@playwright/test` `.spec.ts` 作 feature E2E（既有 `smoke.*.spec.ts` 属冒烟基线，除外）。F27 已落地示范：`e2e/features/publish-workflow.feature`。

---

## Feature 史诗（Tier 1 —— feature-builder 消费单元）

> 每个史诗含：目标 + 验收子项（原 B/O/P 归并，保留 `文件:行号` 锚点）+ 优先级 + 风险 + 设计文档链接。验收子项里的前端细项可由 `feature-dev` 直接取做。

### F41 · 移除 QuickStart 模式、强制真实 Key 与环境变量配置  [P1]  done  ✅（2026-08-26，commit `a11a6c6`；BREAKING CHANGE：运行环境无真实 LLM Key 启动即 fail-fast；平台模型配置 DB 化 `62ede44`；设计文档 features/f41-remove-quickstart-enforce-real-keys.md）
- 设计文档：`features/f41-remove-quickstart-enforce-real-keys.md`（本文件即为设计文档，验收标准 §4）
- 动机：QuickStart 注册 `StubModelClient` 但未替换 `ITenantModelClientResolver`，租户有 BYO 凭据时 `model: "stub"` 被忽略直连真实 OpenAI → 403 `insufficient_user_quota`；且 Stub 体验掩盖真实链路问题。
- 落地：删 QuickStart profile 与环境判断；`DependencyInjection.cs` 仅 `Test` 环境允许 Stub；启动校验 `OpenAI:Key`/`OpenAI:BaseUrl`（Test 豁免）；`IntegrationAppFactory` 环境变量注入真实 Key；CI Secret `OPENAI_API_KEY`；README/BLUEPRINT 同步。
- 后续衍生修复：CI 环境变量映射（`OpenAI__Key` 双下划线）、SpecFlow HttpClient 5 分钟超时、E2E 测试自清理 BYO 凭据——见 CHANGELOG v2.33。

### F34 · 沙箱双层隔离（Docker 默认强隔离 + JobObject/AppContainer 兜底）  [P0 最高优先级]  done  ✅（2026-08-07，分支 `feat/f34-dual-layer-sandbox`；设计文档 features/dual-layer-sandbox-isolation.md + 质量报告 docs/quality/f34-dual-layer-sandbox-gate.md）  ⬆️原置顶  ⚠️中风险（跨 F9/F11 集成；Docker 可用性探测 + 模式选择）

- 设计文档：`features/dual-layer-sandbox-isolation.md`（已建，完整设计文档）
- 动机：来自 `docs/sandbox-isolation-harness-comparison.md` §7「收敛差距建议」——F11 同内核隔离为 fail-safe 开放、真实禁网依赖宿主 ACL；要获 VM 级确定性隔离，应**两层并存**：默认走 Docker 强隔离，无 daemon 时降级到 F11 的 JobObject/AppContainer 并显式告知用户「隔离 weaker」。
- 目标：在 `ISandboxIsolation` 抽象上新增 `DockerSandboxIsolation`（`Provider=Docker` 且守护进程可用时默认启用），复用 F9 已真实化的 `DockerCodeSandbox` 能力（`NetworkMode=none` + 内存限额 + 只读代码挂载 `:ro`）；`ProcessCodeSandbox` 按 `Sandbox.Provider`/`OsIsolation` 选择隔离层。
- 范围边界：
  - 不重复造轮子——Docker 执行路径直接复用 F9 `DockerCodeSandbox`，本 feature 只新增「模式选择 + 探测 + 兜底链路 + 显式告警」。
  - `OsIsolation=Off` 或 Docker 不可用时，明确回退 F11 `JobObjectSandboxIsolation`/`AppContainerSandboxIsolation`，并打结构化日志/响应字段声明隔离强度（strong / weak）。
- 验收要点：
  - Docker 守护进程可用 → `Provider=Docker` 真实容器执行，断言 `NetworkMode=none` 生效（容器内 socket 连接外部失败）、内存限额生效、只读代码挂载（`:ro`）生效。
  - Docker 不可用 / `Provider=Process` → 走 F11 JobObject（默认）+ 可选 AppContainer，行为与 F11 既有测试一致；响应/日志显式标注隔离强度为 weak。
  - 启动时一次 Docker 可用性探测；探测失败不抛异常，静默降级并告警（fail-safe）。
  - `dotnet build` 0/0；全量 `dotnet test` 0 失败；Docker 路径 `SkippableFact`（本沙箱无 daemon，跳过；CI `ubuntu-latest` 实测）。

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
  - **已知残留（非阻断，已拆为独立 feature F9–F12）**：① 真实 Docker 容器隔离（✅ F9 done）；② Skill/MCP 执行器占位（✅ F10 done）；③ 进程模式 OS 层禁网不可强求（以 `NetworkEnabled=false`+语言白名单+超时杀+输出截断缓解；✅ F11 已在 Windows 经 JobObject 资源限额 + AppContainer 真实禁网落地，跨平台 fail-safe 回退）；④ 含 Tool/Code 节点的全链路 e2e 需后端+Web 实例，本沙箱未跑（单元层已覆盖真实执行路径；open=F12）。

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

### F14 · 供应商模型发现（填 Key+Base URL 后拉取可访问模型清单）  [P0 最高优先级]  done  🔴高风险（新增端点 + 鉴权 + 路由 + 前端契约）
- 设计文档：`features/model-discovery.md`（已建，§6 决策 D1 已锁定）
- 目标：用户在「我的凭据」/Agent 配置页填 API Key + Base URL 后，可一键拉取该 provider 账户下所有可访问模型（`GET /v1/models`，OpenAI 兼容），以下拉供选择，免去手动拼模型名。来源 F13 凭据配置 UX 衍生（F13 当前 Model Name 为手动文本框）。
- 核心改造：
  - 后端新端点 `POST /api/v1/tenant/credentials/discover-models`，body `{ provider, apiKey, baseUrl? }`；解析 provider 默认 BaseUrl（OpenAI→`https://api.openai.com/v1`、DeepSeek→`https://api.deepseek.com/v1`，VLLM/Custom 必填 baseUrl）/ `IHttpClientFactory` `GET {baseUrl}/models`（`Bearer`，请求级超时 15s）/ 解析 OpenAI 兼容 `{data:[{id,owned_by}]}` 返回 `List<ProviderModelInfo>(Id,OwnedBy)`；错误 401/403/404/超时/传输 → 400 + 中文原因；`[Authorize(Roles="Admin,Operator")]`；密钥仅探测，不落库不记日志。
  - 新服务 `IProviderModelDiscovery` + `ProviderModelDiscovery`（Infrastructure），DI `AddScoped`；单测 mock `HttpMessageHandler` 覆盖 URL/解析/401。
  - 前端 `CredentialForm`：模型类 Model Name 由 `Input` 改 `AutoComplete`（选项来自发现结果，允许手动自定义）；「拉取模型」按钮（填 Key+BaseUrl 可用，loading+错误）；edit 模式 Key 留空时按钮禁用并要求先填 Key（**D1：不做后端解密存量密钥探测**）。
- 验收子项：
  - 后端 discover-models：OpenAI/DeepSeek 默认 base 补全正确；VLLM/Custom 缺 baseUrl → 400；401/403/404 → 400 中文原因；解析 `id` 正确；空 `data` → 200 空数组；`StubHttpMessageHandler` 单测覆盖。
  - 前端 AutoComplete 下拉填充 + 允许自定义；按钮 loading/错误；edit 模式留空 Key 时按钮提示先填 Key。
- **完成记录（2026-07-28）**：feature-builder 全栈实跑落地。后端 `IProviderModelDiscovery`(Abstractions 接口)+`ProviderModelInfo`+`ProviderModelDiscoveryException`+`ProviderModelDiscovery`(Infrastructure/Models 真实 HttpClient 出站，复用 SerpApiSearchProvider 模式)+`DiscoverModelsRequest`(Api.Models)+`TenantCredentialsController.discover-models`([Authorize Admin,Operator]，只读探测无落库)+DI 注册；无 EF 迁移。前端 `types/index.ts` 加 `ProviderModelInfo`、`api.ts` 加 `discoverProviderModels`、`CredentialForm` 模型类 Model Name 改 `AutoComplete`+「拉取模型」按钮(loading/错误/edit 模式留空 Key 禁用并用 Tooltip 提示先填 Key)。三道质量门 PASS（`.quality-gate.json` 推进 `f14-model-discovery`）；`dotnet test src/AgentPlatform.sln` **255 passed / 0 failed**（含 F14 新增 11 例 ProviderModelDiscovery 单测）；前端 `tsc --noEmit` 0 error + `vite build` 通过。模型一致性：后端 camelCase `{id,ownedBy}`、前端对应 `{id,ownedBy}`。**审查修复 P1**：`ProviderModelDiscovery` 原 `response.Content.ReadAsStringAsync` 位于 try 之外，超时发生在读体阶段会抛未捕获异常→500，已移入 try 并用 `using var response` 全程受请求级超时保护。**审查修复 P2**：`CredentialForm`「拉取模型」disabled Button 用 title 提示禁用原因但 antd v5 吞 hover 导致提示不可见，已用 Tooltip 包裹。
  - e2e（Python UTF-8）：登录 → 填 Key+BaseUrl → discover → 返回模型列表 → 选一个 → 保存 → `GET /tenant/credentials` 列表含该 model。
  - 质量门：build 0/0、`dotnet test` 全绿（含 discovery 单测）、前端 tsc 0 + vitest 全过 + vite build；实现后追加 `.quality-gate.json` notes 并保 `cleared:true`。
- 决策（见 `./model-discovery.md` §6）：D1 edit 模式探测密钥 = 仅用表单现填 Key（不做后端解密存量，用户 2026-07-27 拍板）/ D2 范围仅模型类（搜索无模型列表语义）/ D3 无 schema 变更（不落库，无 EF 迁移）/ D4 安全边界（Admin 专用+用户自有 provider 出站，密钥不落库不记日志）。
- 风险：🔴 高风险（新增端点+鉴权+路由+前端契约，触发 feature-dev 高风险闸口，先设计后实现）；出站请求 SSRF 面（Admin 专用可接受，后续可加域名白名单，不在本范围）；非标准 `/models` 返回容错（缺 `owned_by`/非数组 data 容忍）。

### F15 · 多语言国际化（i18n，暂仅中文 + 英文）  [P1]  done  🟡中风险（前端跨切面文案抽取 + 全局 Provider 注入）
- 设计文档：`features/i18n.md`（已建，§5 决策 D1–D4 已锁定 2026-07-28）
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
- **完成记录（2026-07-28）**：feature-builder 流水线端到端实现并提交（分支 `feat/f15-i18n`）。新增 `src/locales/`（index.ts 初始化 + zh-CN.ts + en-US.ts + config.ts，`en-US.ts` 以 `Resources = typeof zhCN` 类型镜像 + `src/__tests__/i18n-symmetry.test.ts` 运行时对称测试强制 key 一致）；新增 `components/LanguageSwitcher.tsx`（顶栏中文/English 切换 + `localStorage('app-locale')` 持久化）；`App.tsx` `ConfigProvider(antd locale)` + `dayjs.locale` 随 `languageChanged` 事件联动；全站 UI 框架级文案 `t()` 化（导航/登录/页标题/按钮/表单标签/Empty/ErrorState/message），领域数据按 D4 不翻。三道质量门 PASS（`.quality-gate.json` 推进 `f15-i18n`，`cleared:true`）；前端 `tsc --noEmit` **0 error** + `vitest` **30/30 green**（10 测试文件，含新增 config 4 项）+ `vite build` 通过。**审查修复**：P1 `common.total` 双包缺失导致分页泄露原始键串→补键；P2 模块级 `columns` 在组件外调用 `t()` 触发 TS2304→改组件内工厂；P2/P3 多处漏翻硬编码 UI 串→统一 `t()`；P3 en-US 镜像重写 + 新增 `config.test.ts`。codebase-optimizer P3 36 个未引用 i18n key 已 waiver（antd 重叠词由 ConfigProvider 本地化、`errors.*` 预留 D1、`empty.*` 预留 Empty 描述）。质量报告 `docs/quality/f15-i18n-gate.md`。
- 决策（见 `./i18n.md` §5，已锁定 2026-07-28）：D1 后端错误文案 v1 不本地化 / D2 资源用 .ts 对象 / D3 默认 zh-CN / D4 v1 仅 UI 框架级文案（领域数据不做）。
- 风险：🟡 中风险（几乎全前端页文案抽取，工作量大；key 规范需统一）；缓解：先定 common/nav/login 高频命名空间，按 §3.5 优先级分批小步提交；第三方画布(@xyflow/react)内置菜单 v1 可能仍中文，列已知残留。

### F16 · 列表统一改为卡片（Card）形式展示  [P2]  done  🟡中风险（前端多列表页渲染层改造）
- 设计文档：`features/card-layout.md`（已建，§5 决策 D1–D4 已锁定 2026-07-28）
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
- 决策（见 `./card-layout.md` §5，已锁定 2026-07-28）：D1 执行日志改卡片（多列压为卡片元信息）/ D2 详情内子表（step entries/文档列表/Steps）v1 保留 Table / D3 与 F15 协同（F16 直接用 `t()`）/ D4 卡片密度默认大屏 4 列、日志降 3 列。
- 风险：🟡 中风险（几乎所有列表页渲染层，工作量大）；缓解：先落 `EntityCardGrid` 单一基件，再逐页小步替换（每页一提交），优先高频页；信息密度须保关键字段（状态/时间/owner）不丢；与 F15 时序耦合（D3 规避）。
- **完成记录（2026-07-29）**：feature-builder 纯前端实跑落地。新增 `components/EntityCardGrid.tsx`（网格 + Skeleton 加载骨架 + Empty 空态 + 响应式列 normal lg=6 / compact lg=8 + onItemClick + rowKey + density）+ `components/__tests__/EntityCardGrid.test.tsx` 7 项单测。9 个列表页改造为卡片（Agents/AgentConfigurations configsTab/Workflows/Conversations/KnowledgeBases/CredentialManager/ApiKeys/ExecutionLogs(compact)/AgentRoles 两网格）；ResearchPage 故意排除（任务流非实体列表）、详情内子表按 D2 保留 Table。与 F15 协同卡片文案全 `t()`。三道质量门 PASS（`.quality-gate.json` 推进 `f16-card-layout`）；前端 `tsc --noEmit` **0 error** + `vitest` **38/38**（含新增 7 + AgentsPage 契约更新）+ `vite build` 通过。审查修复 P0：`EntityCardGrid` 整卡 `onItemClick` 与卡内交互子元素点击冒泡冲突 → 安全默认拦截（closest button/a/input/select/textarea/[role=button]/[data-no-card-click]）。质量报告 `docs/quality/f16-card-layout-gate.md`。注意：`AgentConfigurationsPage` 与 F17、`AgentRolesPage` 与 F19 强耦合，F16 不改其写路径，由 F17/F19 收口。

### F17 · AgentConfiguration 实例化联动（方案 A 细化）  [P2]  done  🟡中风险（前端 CRUD 补全 + 1 新端点 + RBAC 收敛；不触 EF 迁移）
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
- 决策（见 `./agent-config-instantiation.md` §5，已锁定 2026-07-29）：D1 实例化=前端预填为主、后端 `ConfigurationId` 仅溯源（v1 不强制改聚合/无迁移）/ D2 与 F16 时序（F16 已并入 master，F17 从 master 派生直接复用卡片 UI）/ D3 YAML 编辑器 v1 用 TextArea（不引 Monaco）/ D4 模板字段映射约定（YAML 采纳 `AgentYamlModel` 结构，前端以 template 端点返回为准不自解析）。
- 风险：🟡 中风险（前端 CRUD 跨模态 + 新端点 + AppLayout RBAC）；缓解：后端 CRUD 已就绪仅差 UI、template 端点只读+解析无写副作用、YAML 解析单点服务端；与 F16 渲染冲突见 D2 明确单一 owner 页。
- **完成记录（2026-07-29）**：feature-builder 流水线端到端实现并提交（分支 `feat/f17-agent-config-instantiation`）。后端：新增 `GET /api/v1/agent-configurations/{id}/template`（`[Authorize(Roles="Admin")]`，handler 显式比对 `TenantId`→跨租户 404、YAML 解析容错 try/catch→畸形仅返元数据）；`CreateAgentCommand` 加 `Guid? ConfigurationId` 溯源（handler 最佳努力写审计后缀，不阻断创建、无 EF 迁移）。**设计偏差**：设计文档假设新建 `AgentConfigurationYamlParser`，经验证 `IYamlConfigurationParser`(`Application/Abstractions`)+`YamlConfigurationParserService`(`Infrastructure/Configuration`，YamlDotNet+UnderscoredNamingConvention) 已存在 → 直接复用，无新增 parser 类。前端：`AgentConfigurationsPage` 移除重复凭据 tab + 完整 CRUD（新建/编辑/删除模态，agentTypeCode 取自 `getAgentRoles()`，yamlContent TextArea）+ 抽屉详情 `getAgentConfiguration(c.id)` 取 yamlContent；`AgentsPage` 加「基于模板新建」（`getAgentConfigurations()`→选其一→`getAgentConfigurationTemplate(id)` 预填，并对模板 model 注入合成目录项避免静默 provider 丢失，set `pendingConfigurationId`→`createAgent` 传 `configurationId`）；`AppLayout` Configurations 菜单 Admin-only 与后端对齐；`api.ts`+`types/index.ts` 补全 5 接口与 `Create/UpdateAgentConfigurationRequest`/`ConfigurationAgentTemplate` 类型，`AgentConfiguration` 接口漂移修正（`agentType→agentTypeCode`/`isActive→status`/`createdAt→updatedAt`）。i18n 同步 zh-CN/en-US（`configurations` 重写去 credentials/model/search + 增 agents.fromTemplate 等）。三道质量门 PASS（`.quality-gate.json` 推进 `f17-agent-config-instantiation`，`cleared:true`）；后端 `dotnet build` **0/0** + 全方案 `dotnet test` **260/260**（SpecFlow 41 / Architecture 6 / Application 90 / Infrastructure 102 / Api 16 / Integration 5，含 F17 新增 `GetConfigurationTemplateQueryHandlerTests` 5 项 + `CreateAgentCommandHandlerTests` 增 `IAgentConfigurationRepository` 入参）；前端 `tsc --noEmit` **0 error** + `vitest` **38/38**（11 文件，含 i18n 对称 4 项）+ `vite build` 通过。审查修复 P2：`chooseTemplate` 模板 model 未在 `models` 目录时静默丢 provider → 注入合成目录项兜底。**附带修正（非 F17 范围但阻塞全方案绿）**：`AesGcmEncryptorTests.Decrypt_ThrowsOnTamperedCiphertext` 原 hex-flip 在中间字符为 `'a'` 时 `(char)(c^1)` 得到 `` ` ``（非法 hex）→ 偶发抛 `FormatException` 而非预期的 `AuthenticationTagMismatchException`（约 1/16 概率飘红）；改为全 hex 字符确定性映射到合法异值字符，5 次连跑稳定 6/6。质量报告 `docs/quality/f17-agent-config-instantiation-gate.md`。注意：与 F16 强耦合的 `AgentConfigurationsPage` 写路径已由 F17 收口。

### F18 · Dashboard 图表充实（运行分析看板）  [P1]  done  🟡中风险（新增 analytics 端点 + 前端图表库 + 时间聚合）✅ 2026-07-30 `feat/f18-dashboard-charts`
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

### F19 · Agent Roles 内建标记 + 页面补全 + 分类合并（统一角色目录，DB 为准）  [P1]  done  🟡中风险（角色分类值对象 + 聚合加列 + EF 迁移 + 新增 PUT 端点 + 前端页重写）
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
- **完成记录（2026-07-29）**：feature-builder 全栈实跑落地（分支 `feat/f19-agent-roles-unified`）。后端：`AgentRoleDefinition` 增 `IsBuiltIn`(bool) + EF 迁移 `AddAgentRoleIsBuiltIn`（`defaultValue:false`）；`DatabaseInitializer` 幂等对齐 7 内建（缺失插入/已存非内建 `MarkAsBuiltIn`）+ **legacy→new RoleCode 幂等映射**（architect/developer/tester/pm/tech-writer→新码，修正设计 §3.1「数据连续」错误假设、防存量 Agent 游离）；`AgentType` 降为 `BuiltInRoleCatalog` 镜像 + 架构 parity 测试 3 例强制 DB 为准；`IAgentRepository.CountByRoleAsync` + `AgentRoleSummary.AgentCount` 引用计数；新增 `PUT /api/v1/agent-roles/{roleCode}` + `UpdateAgentRoleDefinitionCommand/Handler`；`DeleteAgentRoleCommand` 重写枚举结局（Deleted/NotFound/BuiltInConflict/InUseConflict，409 拦截内建/被引用）。前端：`AgentRolesPage` 删 `BUILT_IN_ROLES` 硬编码、按 `IsBuiltIn` 分区、新建/编辑/删除模态 + RBAC + `agentCount` 展示；`AgentsPage` 默认 `roleCode`→`development`；`types`/`api`/`locales` 对齐（i18n 对称 4 例 zh-CN 去字面 "Agent"）。**审查修复 P2×2**：`AgentsController` 默认 `RoleCode ?? "developer"`→`"development"`；`DatabaseInitializer` 补 legacy 映射。**三道质量门 PASS**（`.quality-gate.json` 推进 `f19-agent-roles-unified`，`cleared:true`）；后端 `dotnet build` **0/0** + 全方案 `dotnet test` **287/287**（SpecFlow 41 / Arch 9 / App 103 / Api 27 / Integration 5 / Infra 102，含 F19 新增 parity 3 + handler 7 + Api 集成 7）；前端 `tsc --noEmit` **0 error** + `vitest` **38/38** + `vite build` 通过。质量报告 `docs/quality/f19-agent-roles-gate.md`，结构清单嵌入 `features/agent-roles-builtin.md` §7。注意：设计 §3.1 原「存量 Agent code 已与新目录一致、无需迁移」假设不成立（旧码 architect/developer/tester/pm/tech-writer 整体不符），已由 `DatabaseInitializer` remap 兜底。

### F7 · 工作流平台化（program，已分解为 F20–F26）  [P2/P3]  done  （program 框架；子项①已实现，②③④⑤⑥⑦⑧ 已拆为独立史诗 F20–F26）
- 设计文档：`features/workflow-platformization.md`（已建，§0 列出 8 子史诗总览；子项① 已落地于分支 `feat/f7-workflow-versioning`，commit `df79e6f`）
- 目标：把 DAG 画布 MVP 推向生产级平台能力。子项① `done`（版本管理+导入导出）；剩余 7 子项已各自独立成 Tier-1 史诗，详见下方 **F20 / F21 / F22 / F23 / F24 / F25 / F26**：
  - ① 版本管理 + 导入导出 → **F7 子项① 已 done**（见 `docs/quality/f7-workflow-versioning-gate.md`）
  - ② 节点全家桶 → **F20**
  - ③ 触发器 → **F21**
  - ④ 发布为 API / MCP Server → **F22**
  - ⑤ 模板市场 / 示例库 → **F23**
  - ⑥ 执行 Trace / 评估视图 → **F24**
  - ⑦ 工作流调试器 → **F25**
  - ⑧ 企业增强（多工作空间 / 用量仪表盘 / 工作流 diff）→ **F26**
- **设计文档骨架均已生成**（features/node-bundle.md / workflow-triggers.md / publish-api-mcp.md / template-market.md / execution-trace-eval.md / workflow-debugger.md / enterprise-enhancements.md）；各 feature 实现前须先锁定其 §6 决策，不应自创需求。


### F20 · 节点全家桶（Workflow 节点类型扩展）  [P1]  done  ⚠️高风险（破坏性 StepType 枚举扩展 + HITL 审批门 + 运行时 executor + 编排器分支/循环引擎）
- 设计文档：`features/node-bundle.md`（已建，§6 决策 S1–S5 已锁定）
- 目标：补齐 DAG 节点原语——HTTP / Condition / Loop / Variable / SubWorkflow / Delay / UserInput(HITL)，前端调色板+配置面板+后端 executor（Tool/Code/Knowledge 已在 F5 落地）。纯增量节点类型，无新聚合。
- 风险：🔴 StepType 枚举破坏性扩展（全仓 switch 回归）+ HITL 暂停/恢复 + 表达式引擎选型。实现前须锁定 §6（S1 枚举命名 / S3 HITL 方案 / S2 表达式引擎）。
- **完成记录（2026-08-03）**：feature-builder 全栈实跑落地（分支 `feat/f20-node-types`，commit `3f14a63`，已合并 master via PR #17）。新增 7 节点类型（StepType 8–14：HTTP/Condition/Loop/Variable/SubWorkflow/Delay/UserInput(HITL)）；HumanApproval 聚合 + 租户隔离仓储 + EF 迁移 `20260731042445_AddHumanApproval`；Jint 4.1.0 沙箱表达式（2s/200k，AllowClr=false）；Workflow.Reset 重跑语义；前端画布全联动 + i18n。三道质量门全 PASS（`.quality-gate.json` 推进 `f20-node-types`，`cleared:true`）；后端 `dotnet test` **330/330**、前端 qa.mjs OVERALL PASS。审查修复 P2（SetResult 空守卫 ThrowIfNullOrWhiteSpace→500，放宽 ThrowIfNull 同步消解 HTTP 204 空体崩溃）/P3（Delay 取消语义澄清）。Loop 编排器内联执行（RunLoopBodyAsync，无 LoopStepExecutor 死代码）；Condition 经 ApplyBranchSkip+ReachableFrom BFS 算不可达子图；HITL 经 HumanApproval(Pending)+NeedsIntervention→SetState(Paused)→ResolveApproval 恢复。质量报告 `docs/quality/f20-node-types-gate.md`。

### F21 · 工作流触发器（Webhook / 定时 / Chat）  [P1]  done  ✅（后台调度基础设施 + 匿名 Webhook + Chat 链路耦合）
- 设计文档：`features/workflow-triggers.md`（§6 决策已锁定：S1 进程内 BackgroundService 轮询 / S2 独立 ConversationWorkflowBinding 表 / S3 复用现有限流 / S4 完整分布式锁(Redis)+进程内回退）
- 目标（已实现）：工作流被动触发——Webhook（POST /webhooks/workflow/{token}，匿名+限流）/ 定时（cron + BackgroundService 调度，分布式锁防重）/ Chat（会话绑定触发）。多租户隔离 + 审计。
- 交付：后端 Domain/Application/Infrastructure/Api 全栈 + 前端触发器 Drawer & 会话绑定 UI + 中英 i18n；三道质量门全 PASS（ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer），详见 `docs/quality/f21-workflow-triggers-gate.md`。

### F22 · 发布工作流为 API / MCP Server  [P1]  done  ⚠️高风险（API Key 鉴权复用 + MCP 动态注册 + 外部输入隔离）
- 设计文档：`features/publish-api-mcp.md`（§6 决策 S1–S4 已于 2026-08-03 全部锁定）
- 目标：一键发布工作流为 API Key 鉴权的 HTTP 端点 + 暴露为 MCP tool（复用现有 ApiKey / ToolDefinition）。多租户隔离 + 审计。
- 风险：🔴 现有 API Key 中间件复用 + MCP 动态注册。实现前须锁定 §6（S1 鉴权复用 / S2 MCP 形态）。
- **完成记录（2026-08-03）**：feature-builder 全栈实跑落地（分支 `feat/f22-publish-api-mcp`）。后端：新增 `PublishedWorkflow` 聚合（`ITenantScoped`，`Slug` 租户内唯一 + `Id ValueGeneratedNever()`）+ `PublishMode` 枚举（Api/Mcp）+ `PublishedWorkflowException` + `IPublishedWorkflowRepository`；5 个 handler（Publish/Unpublish/GetPublishStatus/ListMcpTools/Run，均为 `ICommand<T>` 经 UoW 自动提交）；`PublishedWorkflowConfiguration` + 迁移 `20260803035042_AddPublishedWorkflow`；`PublishedWorkflowsController`（slug 端点）+ `McpController`（平台内 JSON-RPC 2.0 `tools/list`/`tools/call`，无独立进程/端口）+ `PublishedWorkflowExceptionHandler`（RFC 9457）；`AuditActionType` 增 `PublishWorkflow`/`UnpublishWorkflow`/`RunWorkflow`。前端：`WorkflowsPage` 发布管理 Drawer（发布/取消/查看 slug+端点+绑定 Key+启停 Tag，inputSchema + mode + key 表单）+ `api.ts`/`types`/`locales` 中英 i18n 对称。**§6 决策落地**：S1 复用现有 `ApiKeyAuthenticationHandler`（slug/MCP 端点 `[Authorize(AuthenticationSchemes="ApiKey")]` + `PerApiKey` 限流）；S2 平台内 MCP tool（v1 无独立部署）；S3 用户自定义 `InputSchema`（运行时 `required` 校验）；S4 仅返回最终输出。新增 F22 后端测试 18 例（Application 16 + Api 2：发布/取消/状态/MCP 列表/运行隔离/鉴权边界）+ 修复 N+1。**三道质量门 PASS**（`.quality-gate.json` 推进 `f22-publish-api-mcp`，`cleared:true`）；后端 `dotnet build` **0/0** + 全方案 `dotnet test` **348/348**（SpecFlow 41 / Arch 9 / App 141 / Infra 123 / Api 29 / Integration 5）；前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS。质量报告 `docs/quality/f22-publish-api-mcp-gate.md`，结构清单嵌入 `features/publish-api-mcp.md` 末尾。注意：feature doc 原草拟 `IMcpToolProvider` 命名与落地 `McpController` 机制名差异（仅措辞，S2 行为一致）；控制器 happy-path 端到端测试待补 seed。

### F23 · 模板市场 / 示例库  [P2]  done  🟡中风险（种子数据 + 克隆端点 + 前端画廊 · 2026-08-05 全阶段 DONE）
- 设计文档：`features/template-market.md`（§6 决策已锁定 S1–S6；含内嵌《Phase Quality Gate Checklist》）
- 目标：内置 5–10 行业模板，「一键克隆为我的工作流」（复用 F7 ① 快照重建）。平台级共享 + 克隆归当前租户。
- 风险：🟡 种子图须过 ValidateGraph、克隆后 Agent 绑定缺失降级。实现前须锁定 §6（S1 来源 / S2 存储 / S3 Agent 缺失处理）。
- **完成记录（2026-08-05）**：feature-builder 全栈实跑落地（分支 `feat/f23-template-market`）。后端：新增平台级 `WorkflowTemplate` 聚合（**刻意不** `ITenantScoped`，S2）+ `WorkflowTemplateCategory` 枚举（General=0…DataAnalysis=7，硬编码 S4）+ `IWorkflowTemplateRepository`；`WorkflowTemplatesController` 四端点（`GET /` 分类+关键词 / `GET /categories` / `GET /{id}` 含预览图 / `POST /{id}/clone` `[Authorize(Roles="Admin,Operator")]`）；`CloneWorkflowTemplateCommandHandler` 走 F7 ① 快照重建 → `ReplaceGraph`→`ValidateGraph`，节点 Agent 全解绑（S3）、归属当前租户（S2）、审计 `CloneTemplate`（S6）；`DatabaseInitializer` 幂等种子 8 模板（固定 Guid `22222222-…-201..208`，覆盖全 8 分类，图均过 `ValidateGraph`）；迁移 `20260805043045_AddWorkflowTemplate`（`Id ValueGeneratedNever()`）。前端：`TemplateMarketPage`（卡片网格 + 分类 `Select` + 关键词 `Input.Search` + 预览 `Drawer` + RBAC 克隆 `Modal.confirm`→跳转 `/workflows/{id}`）+ `api.ts`/`types`/`locales`（中-en i18n 对称）/ `App.tsx` 路由 / `AppLayout.tsx` 菜单。**审查修复 P1×1**：`getWorkflowTemplates` 原将 `keyword:null` 入参致初始加载空白，改为条件 `params`。**三道质量门 PASS**（`.quality-gate.json` 推进 `f23-template-market`，`cleared:true`）；后端 `dotnet build` **0/0** + F23 单测 **7/7** + 架构测试 **9/9**；前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS。质量报告 `docs/quality/f23-template-market-gate.md`，结构清单嵌入 `features/template-market.md` 末尾。已知残留（非阻断）：BDD e2e（模板列表/预览/克隆门控）属增强，由后端 7 单测 + 前端 qa.mjs 等价覆盖。

### F24 · 执行 Trace / 评估视图  [P1]  done  ✅（2026-08-05 · feat/f24-execution-trace · dotnet build 0/0 + 12 单测 + qa.mjs OVERALL PASS + 三道质量门）
- 设计文档：`features/execution-trace-eval.md`（§6 决策 2026-08-05 锁定；v2.18 收口）
- 目标：节点级 Trace（耗时/token/节点类型/IO，复用 ExecutionLog.Entries）+ 数据集回归评估（对标 LangSmith/Langfuse）。多租户隔离 + 审计。
- 落地：ExecutionLogEntry 增 TokensIn/TokensOut/NodeType 三列（迁移 ExtendExecutionLogEntry）+ ExecutionLogDetailPage 三列；EvaluationDataset(ITenantScoped) 聚合 + 6 端点 + RunEvaluation（克隆工作流逐 case 跑编排、Exact/Contains 比对、汇总通过率/逐 case 报告）+ 前端 EvaluationDatasetsPage（CRUD+运行+报告）+ i18n 中/en 对称。
- 已知残留（非阻断）：①节点级 Input 采集 v1 不做；②Token 实际落库依赖编排器对评估克隆工作流产生 ExecutionLog（与 F20 Trace 共用管线，单测 mock 验证求和）；③BDD e2e（评估门控）属增强。

### F25 · 工作流调试器（变量监视 + 单步重跑 + 错误分支）  [P1]  done  🟡中风险（变量持久化需新增列+迁移；引擎能力 Pause/Resume/RetryStep/RollbackTo/GetState+RunNode 已存在，零引擎侵入 · 2026-08-06 起 feat/f25-workflow-debugger · 2026-08-06 全栈闭环）
- 设计文档：`features/workflow-debugger.md`（§6 决策已锁定初稿，待用户确认；实测引擎已具备调试内核）
- 目标：调试运行模式——变量监视（跨节点累积 Blackboard 持久化）+ 单节点运行/重跑（override）+ 错误分支恢复 + 状态/变量查看 + 会话重置。复用 F24 Trace。
- 风险：🟡 变量持久化需 Workflow 新增 `DebugVariablesJson` 列 + 一次 EF 迁移（最小）；API 暴露既有引擎能力，无内核改动。v1 不做引擎级单步全链（列 v2）。

### F26 · 企业增强（多工作空间 / 用量仪表盘 / 工作流 diff）  [P2]  done  🟢低风险（**v1 仅用量仪表盘 + 工作流 diff 已闭环**；多工作空间 = 第二租户维度独立排期，未触碰 ITenantScoped/TenantProvider · 2026-08-06 起 feat/f26-enterprise-enhancements · 2026-08-06 全栈闭环）
- 设计文档：`features/enterprise-enhancements.md`（§6 决策锁定：v1 仅仪表盘+diff；状态 done v1）
- 目标：① 用量仪表盘（per-workflow 执行/成功率/token/时延，7/14/30 天）✅ ② 工作流 diff（稳定键 Name/边端点比对两版本）✅ ③ 多工作空间隔离切换（二级维度）⏸ 独立排期。
- 风险：🟢 v1 纯增量低风险（无引擎/租户体系侵入）；多工作空间仍 🔴 破坏性极大（全聚合加 WorkspaceId + query filter + TenantProvider 体系），独立排期。


### F27 · BDD 集成测试统一（Reqnroll + 文件 SQLite + Playwright E2E）  [P1]  done  ⚠️高风险（测试架构改造 + SpecFlow→Reqnroll 迁移 + 新增前端 E2E 基建 · 2026-08-04 全阶段 DONE）
- 设计文档：`features/bdd-integration-design.md`（已建，2026-08-03 确认设计 + §7 例外默认 B）
- 目标：把 **BDD 重新定义为「最终集成测试层」** = 真 HTTP（走完整管线）+ 真 DB（**文件 SQLite，明确排除 Api.Tests 现行 in-memory**）+ 前端 E2E（Playwright 真浏览器）；**现有 41 例 SpecFlow 域级测试（假 Repository/域内对象）全量迁移到 HTTP+DB 契约**。
- 核心改造：
  - 测试基座：`IntegrationAppFactory : WebApplicationFactory<Program>`（环境 `Integration` + 文件 SQLite `test-integration.db`）+ `IntegrationSeeder`（集成租户/用户/ApiKey/示例工作流）+ `AuthHelper`（发布类走 JWT、运行类走 `X-Api-Key`）。
  - SpecFlow→Reqnroll（`Reqnroll` + `Reqnroll.xUnit` + `Reqnroll.Tools.MsBuildGeneration`；`using TechTalk.SpecFlow`→`using Reqnroll`；删旧 `.feature.cs` 交生成器重出）。
  - 前端 E2E：新增 `src/AgentPlatform.Web/e2e/`（Playwright + `publish-workflow.spec.ts`，全链路 UI 发布→调用）。
  - 编排：`scripts/integration` + `deploy/*.yml` 增 `integration` job 作合并前最终闸门；`.quality-gate.json` 增 `bdd: PASSED`。
- 决策（2026-08-03 锁定）：框架 Reqnroll / 集成 DB 文件 SQLite / 前端 E2E Playwright / 旧 41 例全迁移；**§7 例外默认 B**：`WorkflowStateMachine` 重试/回滚内部无公开 HTTP 表面 → 走「域集成」连真 DB 但经应用层命令驱动（不为测试加生产端点；若坚持全 HTTP 才走方案 A 受控测试端点）。
- 验收子项：所有 BDD 经真 HTTP+文件 SQLite 全绿（零 mock Repository、零 in-memory）；41 例 + F22 新场景全绿（Reqnroll 报告）；Playwright E2E 覆盖 F22 前端全链路绿（HTML 报告 + trace）；`scripts/integration` 一键编排后端 BDD + 前端 E2E + 卸载；CI 合并前闸门通过。
- 风险：🔴 测试架构改造 + 41 例迁移工作量大；`WorkflowStateMachine` 内部行为 HTTP 不可达（已定例外 B）；限流（`PerApiKey`）干扰 → 基座移除/白名单测试 key；种子 ApiKey 明文须经 `IApiKeyEncryptionService` 加密落库（复用 F13 基件）；前端 E2E 与后端种子一致性（共用 `IntegrationSeeder` 常量）。
- 分阶段：A 基座（Reqnroll+文件 SQLite+种子+AuthHelper）/ B 迁移 41 例 / C F22 BDD 6 场景 / D 前端 E2E / E 编排+CI（详见设计文档 §9）。

### F28 · 历史 feature BDD 测试覆盖补全（按功能域分组，全量）  [P0]  done  ⚠️高风险（全 8 功能域后端 Reqnroll BDD + 前端 playwright-bdd E2E · 2026-08-04 全阶段 DONE）
- 设计文档：`features/bdd-coverage-design.md`（已建，2026-08-04 现状盘点 + §2 八批次表 + §4 验证策略 + §7 实施记录）
- 目标：为「已实现但缺 BDD 集成测试」的 8 大功能域补齐 **后端 Reqnroll BDD（真 HTTP + 文件 SQLite）** 与 **前端 playwright-bdd E2E（真浏览器，zh-CN 断言）**，按风险/价值分批（B1 Auth/RBAC → B8 Agent 生命周期），不要求与单个 feature 史诗 1:1 对应。
- 后端 BDD（B1–B8，114 场景全绿）：`auth-rbac` / `tenant-credentials` / `workflow-management` / `conversation-chat` / `knowledge-base` / `research-agent` / `analytics` / `agent-lifecycle`(+`agent-configurations`)；复用 `IntegrationAppFactory`/`IntegrationSeeder`/`AuthHelper`/`CommonSteps`，双租户隔离断言复用 `IntegrationConstants` 固定 Id+ApiKey。**根因修复**：`TenantModelClientResolver` 在 `ModelClient:Provider=Stub` 时短路返回空解析，消除 B2 启用 BYO 凭据触发的真实 LLM 20s 超时 500（与 F28 Stub 契约一致）。
- 前端 BDD（B1–B8 + 转换 create-agent/page-polish + publish-workflow，11 feature / 22 场景 @e2e 全绿）：`login-auth` / `credentials` / `workflow-crud` / `conversation` / `knowledge-base` / `research` / `dashboard` / `agent-crud` + 转换 `create-agent` / `page-polish`；zh-CN 断言对齐默认 locale；遗留 `create-agent.spec.ts`/`page-polish.spec.ts`（英文断言，与 zh-CN 错配）删除并改写为 BDD；`smoke.*.spec.ts` 保留为冒烟基线（不含 @e2e）。**契约修复**：`playwright.config.ts` 设 `testDir` = `defineBddConfig()` 返回的 `outputDir`（playwright-bdd 9.x 要求 `project.testDir == BDD outputDir`，否则运行期 `BDD config not found`）；新增 `appsettings.Integration.json`（`ModelClient:Provider=Stub` + `StubResponse` + 关限流）使 E2E 后端确定性（不触真实 LLM）。
- 编排/闸门：`scripts/integration.mjs --e2e` 先 bddgen 再 `playwright test --grep @e2e`；`safeCleanDir` 逐文件清理 test-results/playwright-report 绕过沙箱批量删除护栏。
- 验收：后端 `dotnet test` 114/114 全绿；前端 `node scripts/integration.mjs --e2e` 全绿（后端 BDD 114 + 前端 BDD 22）；三道质量门 0 open；`.quality-gate.json` 推进 `f28-bdd-coverage`，含 `bdd:PASSED` + `frontendE2e:BDD` + `cleared:true`。
- 风险：跨场景数据污染（各 feature Background 隔离 + `IntegrationAppFactory` 单例）/ 租户隔离（复用固定 Id+ApiKey）/ Stub 模型（仅验链路与鉴权，不验真实 LLM）/ 前端 locale（全 zh-CN）。

### F8 · 差异化优势产品化（Negotiation + Critic）  [native]  done  ✅（Phase 6 产品化闭环 / 编排模式 Segmented 选择器 + 协商可见指示 + 脚手架；后端原语零改动；分支 feat/f8-negotiation-productization，设计文档 features/negotiation-productization.md，质量门 docs/quality/f8-negotiation-gate.md · 2026-08-11 DONE）
- 设计文档：`features/negotiation-productization.md`（已建，完整设计文档）
- 目标：后端已具备 Negotiation 协商式多智能体 + Critic 收敛原语，待产品化画布「Agent-Team / Negotiation」专属模式（多 Agent 节点 + Critic + 收敛终止条件）。
- 实现：编排模式 `Segmented`（auto 省略 preset 由 DetectPreset 识别 / sequential→int0 / negotiation→int1）；协商模式可见紫色 Tag；`scaffoldAgentTeam()` 一键生成 Start→Architect→Developer→Critic→End；`OrchestrationPresetMode` 类型与 int 收发模型一致性；zh-CN/en-US 三键 i18n；BDD E2E `agent-team-negotiation.feature` 两次保存并运行断言 Completed。
- 验收：tsc/vite build/eslint 0 error；三道质量门 0 open；`.quality-gate.json` 推进 `f8`，含 `cleared:true` + `codebaseOptimizer`。
- 说明：**保留，勿稀释**——Dify/n8n 无此原生原语，是本平台差异化壁垒。

### F9 · 代码沙箱容器隔离（DockerCodeSandbox 真实化）  [P2]  done  ✅（Phase 6 行动层 / Docker.DotNet 真实容器执行；分支 feat/f9-docker-sandbox，设计文档 features/sandbox-docker.md；质量门 docs/quality/f9-docker-sandbox-gate.md）
- 设计文档：`features/sandbox-docker.md`（待建）
- 来源：F5 残留 ①（A2 进程沙箱已真实化；`src/AgentPlatform.Infrastructure/Sandbox/DockerCodeSandbox.cs` 现为显式抛异常占位，需补真实容器执行）。
- 目标：在有 Docker 守护进程的环境，用 `Docker.DotNet` 真实拉起隔离容器执行用户代码，提供比进程沙箱更强的文件系统 / 网络 / 资源边界。
- 验收子项：
  - 引入 `Docker.DotNet` 依赖；`DockerCodeSandbox` 由抛异常改为真实 `RunCodeAsync`/`RunCommandAsync`：镜像拉取/创建、挂载代码文件、容器内运行、捕获 stdout/stderr/ExitCode、资源限制（cpu/mem）、超时 kill、输出截断至 `SandboxSettings.MaxOutputBytes`。
  - `Sandbox:Provider=Docker` 时经 DI 条件注册切到真实 `DockerCodeSandbox`（F5 已留条件注册位 `DependencyInjection.cs`）。
  - 真实副作用单测：需在提供 Docker 守护进程的 runner 上跑（本开发沙箱无 Docker，该 feature 门禁须在含 Docker 的 CI 跑，或提供可跳过集成测试标记）。
  - 默认 `Provider=Process` 不变，保证无 Docker 环境仍可运行。

### F10 · A1 残余执行器真实化（Skill + MCP）  [P2]  done  ✅（Phase 6 行动层 / SkillPackageExecutor 经 Semantic Kernel 真实调用 + McpClient 经 ModelContextProtocol 2.1.0 真实连接列举调用；分支 feat/f10-executor-realization，设计文档 features/executor-realization.md；质量门 docs/quality/f10-executor-realization-gate.md）
- 来源：F5 残留 ②（F5 仅真实化 `NativeToolExecutor`；`src/AgentPlatform.Infrastructure/Tools/SkillPackageExecutor.cs` 与 `McpClient.cs` 保留 `// TODO(Phase6)` 占位，仍伪造成功）。
- 目标：让 SK 技能包与 MCP 工具真正执行，补全 Agent 三类动作源（Native / Skill / MCP）的真实副作用。
- 验收子项：
  - **Skill**：`SkillPackageExecutor` 接 SK runtime（Semantic Kernel plugin 加载 / 技能包运行器），按 `ToolDefinition.SkillPluginName` 真实调用插件函数，回真实结果与失败；契约不变（`IToolExecutor`/`ToolExecutionResult`）。
  - **MCP**：`McpClient` 接 MCP client（SSE / stdio transport），连接外部 MCP server，按 `ToolDefinition` 列出/调用工具，回真实结果；含连接失败/超时精准回打。
  - 单测：各自真实执行路径（SK 用内存插件或 mock runtime；MCP 用本地 mock MCP server / test transport）覆盖成功/失败。
  - 两者可独立排期（F7「发布为 MCP Server」复用此能力）。

### F11 · 沙箱 OS 级隔离增强（进程沙箱禁网/资源限额）  [P2]  done  ✅（Phase 6 行动层 / Windows JobObject 资源限额 + AppContainer 真实禁网，fail-safe 回退；分支 feat/f11-sandbox-os-isolation，设计文档 features/sandbox-os-isolation.md；质量门 docs/quality/f11-sandbox-os-isolation-gate.md）
- 设计文档：`features/sandbox-os-isolation.md`（已建，分支 feat/f11-sandbox-os-isolation）
- 来源：F5 残留 ③（`SandboxSettings.NetworkEnabled=false` 在进程沙箱仅为声明，未在 OS 层强制；语言白名单 + 超时杀 + 输出截断为缓解项）。
- 目标：让 `Process` 沙箱在不引入 Docker 的前提下获得 OS 级网络隔离与资源约束，使 `NetworkEnabled=false` 真正生效。
- 验收子项：
  - Linux：`unshare`/`clone` 网络命名空间（或 `NetworkEnabled=false` 时禁网）+ cgroups v2 资源限额（cpu/mem/pids）+ 可选 seccomp 系统调用过滤。
  - macOS：`sandbox-exec` 配置（禁网 / 限文件访问）。
  - Windows：`AppContainer` / 作业对象（Job Object）资源限额。
  - 与 `SandboxSettings`（NetworkEnabled / TimeoutSeconds / AllowedLanguages / MaxOutputBytes）联动；跨平台抽象，失败安全（不支持平台回退现有缓解项并告警）。
  - 单测：在 CI 对应平台断言禁网生效（e.g. 代码尝试 socket 连接 → 失败）。

### F12 · Tool/Code 节点全链路 e2e  [P3]  done  🟢低风险（测试基础设施 · feature-builder 全流程 · 分支 `feat/f12-tool-code-e2e`）
- 设计文档：`features/tool-code-e2e.md`（已建，完整设计文档）
- 来源：F5 残留 ④（单元层已覆盖真实执行路径；含 Tool/Code 节点的端到端需后端+Web 实例，本开发沙箱未跑）。
- 目标：起真实后端 + Web 实例，跑一条含 Tool 节点（真实 HTTP）与 Code 节点（真实 python/node 子进程）的工作流，断言端到端 stdout/响应回填与节点状态。
- 验收子项：
  - 新建/扩展集成测试：用 `WebApplicationFactory` 起后端 + 本地 Mock HTTP 端点，构造含 `StepType.Tool`/`StepType.Code` 的 `WorkflowNode`，经 `WorkflowOrchestrator` 跑全流程，断言 `StepExecutionResult.Outcome` 与 `Output`。
  - 前端联动（可选）：用 Playwright/Cypress 在 Web 实例上拖出 Tool/Code 节点、配置、运行、断言画布节点状态与输出面板。
  - 纳入 CI e2e 阶段；本沙箱无 Docker 仍可跑（python/node 子进程 + 本地 HTTP 端点均可用）。

---

## 第二期 · 真 Agent Harness 升级（第一期已于 2026-08-11 全部完成，现已解锁）

> ⛔ **第二期 = 真 Harness 升级路线图**：源于 `docs/agent-harness-blueprint.md`（Phase 7–11）与 `phases/phase-7-*.md`～`phases/phase-11-*.md`。
> **硬性阻塞（已满足）**：本组 F29–F34 **须第一期全部任务完成后方可开工**——上方 `## Feature 史诗（Tier 1）` 分组内 29 个史诗已于 2026-08-11 全部 `done`、无遗留 `open` 项，阻塞条件**已满足**，可进入 feature-builder 取数。
> **状态约定**：本组统一标 `open ⛔blocked(1期)`；在 1 期清零前**不建 `features/<id>.md`、不建 `feat/<id>` 分支、不跑 feature-builder**。（2026-08-11 更新：第一期已清零，阻塞解除；状态标记待 2 期正式启动时清理为 `doing`。）
> **编号说明**：F27/F28 已被「BDD 集成测试统一 / 历史 feature BDD 覆盖补全」占用（已 done），故本组顺延为 **F29–F34**。其中 **F29 = Agentic Agent 控制循环原语（置顶 · 最高优先级 · 独立轨道，先于 Phase 7–11 启动）**，原 F29–F33 顺延为 **F30–F34**（执行持久化 / Agent 运行时实体化 / 消息总线 / 语义记忆 / 在线评估门禁）。

### F29 · Agentic Agent Primitive（自主 Agent 控制循环原语）  [P0 置顶]  done  ✅1期解锁  🔴高风险（范式跨越：DAG 静态编排 → 模型自主循环 + 工具调用通道 + 安全护栏）
- 设计依据：`features/agentic-agent-primitive.md`（已建，完整设计文档）+ 用户 2026-08-12 拍板「二期第一个 feature，置顶」；独立轨道（拟 Phase 12），先于 Phase 7–11 启动。
- 目标：把「agent 配置实体」升级为「真 agent」——给定目标 + 允许工具白名单，模型自主循环（plan→act→observe→reflect）决策、调用工具、观察结果、再决策，直到停止条件。这是 dev-agent 与 Codex/Claude Code/WorkBuddy 等真 harness 的范式级差距根因，也是产品差异化核心。
- 现状核实（代码事实）：`IModelClient.ChatAsync` 仅返回文本 `ModelResponse`（无 ToolCalls）——最大 blocker；`SemanticKernelModelClient` 基于 SK 但未注册 function/未解析 `ToolCallContent`（SK 原生支持，仅缺接线）；`ToolCallingDispatcher`+`IToolRegistry`+3 个 `IToolExecutor`(Native/Skill/MCP) 已具备工具执行半边；`ChatMessage` 已预留 `ToolCallId`/`ToolName`；`ChatStreamAsync` 已存在；沙箱 substrate(F9/F10/F11/F34) 可作 workspace 工具底座。地基 ~65% 已埋。
- 核心改造（P0）：① 模型工具调用通道（扩 `IModelClient`+SK function 注册+解析 `ToolCallContent`，不自 Invoke，由平台循环接管）/ ② ReAct 控制循环引擎（新 `AgenticOrchestrator`，循环组消息→调模型→有 tool call 则 `ToolCallingDispatcher.DispatchAsync`→结果回灌→无 tool call 判停+迭代硬上限）/ ③ agent 配置字段+迁移（`AllowedToolNames`/`MaxIterations`/`StopCriteria`，与 F31 协同）/ ④ agent workspace/FS 工具（新增 `WorkspaceToolExecutor`：read/write/edit/run_command/list_dir，在现有代码沙箱 substrate 内执行，真 coding 自主硬前提）/ ⑤ 安全护栏（路径白名单+命令黑名单+破坏性操作 HITL 确认+硬迭代/成本上限复用 F13 `ICostController`+审计复用 F24 Trace）。
- P1 项（依赖二期其他项）：⑥ 长程 durable（依赖 F30）/ ⑦ 流式可中断 UX（复用 `ChatStreamAsync`+SSE）/ ⑧ compaction（部分=F33）。
- 衔接：依赖 **F31（Agent 运行时实体化）**（agent 实体是控制循环消费底座，F29 ① 在其上扩展）+ **F30（执行持久化）**（长程检查点）；**不替代 DAG**——自主 agent 最自然形态是 `StepType.Agentic` 节点经 `SequentialOrchestrator` 调度（混合编排）。
- 验收子项（v1 最小闭环）：① 模型吐 ToolCalls（单测 mock SK）/ ② 控制循环 standalone 跑通（StubModelClient+内存工具）/ ③ 三字段落库+迁移+种子/ ④ Workspace 工具沙箱内真实读写跑（单测走 ProcessCodeSandbox）/ ⑤ 护栏单测/ ⑥(P1) durable 接 F30/ ⑦(P1) 流式可中断。
- 诚实风险：模型 function-calling 质量依赖 DefaultModelId；跑飞/成本需硬上限；「跑完≠正确」需人审+F24 eval；DAG 不被替代。
- 质量门：三道门（ddd-code-reviewer/ddd-phase-quality-gate/codebase-optimizer）；高风险闸口（IModelClient 契约/工具调用租户上下文）先设计后实现；`.quality-gate.json` 推进 `f29-agentic-agent-primitive` 含 `cleared:true`+`codebaseOptimizer`；测试工程纳入 `AgentPlatform.sln`。
- 最小验证路径：先做「research agent」standalone（目标→调 2-3 工具→循环→产出）→ 再包 `StepType.Agentic` 节点 → 再补前端（Agent 配置页加允许工具/最大迭代 + 运行页展示思考/工具流）。
- 优先级：P0（置顶，二期第一个 feature，最高优先级）。

### F30 · 执行持久化（Durable Execution）  [P0]  done  🔴高风险（运行时范式跨越：请求内同步 → 可挂起/恢复 durable）
- 设计依据：`phases/phase-7-durable-execution.md` + `docs/agent-harness-blueprint.md` §Phase 7（正式 `features/f30-*.md` 待 1 期完成后建）
- 目标：将 `SequentialOrchestrator.RunToCompletionAsync` 的请求内同步执行改造为可挂起/恢复的 durable 执行；检查点落 `ExecutionLog`；in-flight 状态由进程内 `ConcurrentDictionary` 迁移至 DB；`WorkflowScheduler` 升级为 durable 驱动器。
- 验收子项：
  - **D1** durable 框架选型决策（自建检查点 vs Temporal / Workflow Core）→ 决策关锁定后方可实现。
  - **①** ExecutionLog 检查点机制（per-step 持久化，复用既有 per-step SaveChanges）。
  - **②** 挂起 / 恢复 API（暴露 Resume / Suspend 端点，替代进程内 Timer 驱逐）。
  - **③** in-flight 状态外置（ConcurrentDictionary → DB-backed RunningExecution）。
  - **④** WorkflowScheduler 升级为 durable 驱动器（轮询触发器 → 驱动持久化执行）。
- 优先级：P0（与 F31 组成最小闭环，消除「无 durable 执行」最大差距；并为 F29 长程 agent 提供检查点支撑）。

### F31 · Agent 运行时实体化 + 模型接通  [P0]  done  🔴高风险（agent 配置当前不生效）
- 设计依据：`phases/phase-8-agent-runtime.md` + `docs/agent-harness-blueprint.md` §Phase 8；设计文档 `features/f31-agent-runtime.md`（已建，§6 决策 D1–D4 锁定，§8 完成记录）
- 目标：修复 `AgentCallStepExecutor.cs:50` 硬编码（当前忽略 agent 的 `SystemPrompt`/`ModelEndpoint`，直接硬编码 prompt + `DefaultModelId`）；接通既有 `ModelRouter` + `TenantModelClientResolver`；补 Agent 种子字段。
- 验收子项（v1）：
  - **①** executor 接管 agent 配置（SystemPrompt / ModelEndpoint 真正生效）。→ ✅ 按 AssignedAgentId 加载聚合、SystemPrompt 进消息、PreferredModel 走 agent 模型名；5 例单测锁定
  - **②** ModelRouter + TenantModelClientResolver 接通到 agent 级（候选回退 / 租户 BYO）。→ ✅ AgentCall+Critic 双通道改经 RouteAsync；空候选新增 ModelNotConfiguredException 可操作报错
  - **③** Agent 种子字段补全（支持运行时实体化）。→ ✅ 核实字段已齐备（SystemPrompt+ModelEndpoint VO 已映射），零迁移
- 延后项（依赖 D4 决策）：Agent 上下文隔离 / 运行时实体深化 → 独立排期，不阻塞 v1。
- 优先级：P0（与 F30 组成最小闭环）。本 feature 的 agent 配置实体化是 **F29（Agentic Agent Primitive）** 控制循环消费的底座；F29 的 ① 模型工具调用通道在 F31 之上扩展，二者共同构成「可自主 agent」。
- **完成记录（2026-08-25）**：feature-builder 全栈闭环（分支 `feat/f31-agent-runtime`，基于 f30）。附带修复三项：① F30 回归——陈旧 RunningExecution 租约阻断重跑/恢复（TryAcquireLease 移除 Running-only 门禁，触发器集成 2 例转绿实证）；② 领域 bug——TryAcquireLease 属性自比恒 true 致多实例租约守卫失效（改参数 vs 持有者比较 + Rehydrate 工厂）；③ 生产缺陷——ResolveBashPath 兜底命中 System32 WSL 桩致无 Git Bash 的 Windows 全部 run_command 必败（排除系统目录桩 + echo 实测探针）。全绿：App 214 / Infra 147+6skip / Api 35 / Arch 9，build 0/0。三道质量门 PASS。

### F32 · Agent 消息总线 + 多 Agent 协作  [P1]  done  🟡中风险（依赖 F31 agent 实体化）
- 设计依据：`phases/phase-9-agent-message-bus.md` + `docs/agent-harness-blueprint.md` §Phase 9；设计文档 `features/f32-agent-message-bus.md`（§7 决策 D1–D5 锁定，§8 完成记录）
- 目标：引入 agent 间消息原语；进程内 `Channel<T>` 起步总线；`NegotiationOrchestrator` 真并行推理；handoff / 幂等 / 活锁防治。
- 验收子项：
  - **D2** 总线传输决策 → ✅ in-process Channel<T>（有界 256 背压），SCOPED 隔离，broker 留 Phase 11
  - **①** 消息总线基础设施 → ✅ IAgentMessageBus + InProcessAgentMessageBus + AgentMessageLog 写穿持久化（迁移 AddAgentMessageLog）
  - **②** 多 Agent 协作编排 → ✅ Negotiation 双模式：绑定 agent 时 Task.WhenAll 真并行提案（时间窗重叠实证）+ critic 收敛；无绑定 agent 诚实降级串行
  - **③** handoff / 幂等 / 活锁防治 → ✅ critic 拒绝自动 Critique+Handoff（反馈上下文随 payload）；TryMarkConsumed 条件更新幂等 + 未消费重投；预算/停滞/指纹三防线熔断 Paused+告警
- **完成记录（2026-08-25）**：feature-builder 全栈闭环（分支 `feat/f32-agent-message-bus`，基于 f31）。附带修复：`nvarchar(max)` 列类型在 SQLite EnsureCreated/MigrateAsync 的 DDL 语法错误（Api 31 例连锁失败根因）——统一改 `text` 并回改 F30 两迁移。新增测试 7 例（总线 4 + 协作 3）；全绿 App217/Infra151+6skip/Api35/Arch9，build 0/0，前端零改动。三道质量门 PASS。

### F33 · 语义记忆层  [P1]  done  🟡中风险（依赖既有 IVectorStore）
- 设计依据：`phases/phase-10-semantic-memory.md` + `docs/agent-harness-blueprint.md` §Phase 10；设计文档 `features/f33-semantic-memory.md`（§4 决策 D3/D2'/D4'/D5'，§6 完成记录）
- 目标：从「文件注入式记忆」升级为语义记忆引擎；`IEmbeddingGenerator` 生成 embedding；episodic 写回；自动 compaction；复用 `IVectorStore`（Pg/InMemory）；租户向量隔离。
- 验收子项：
  - **D3** 向量后端决策 → ✅ 复用 IVectorStore（Pg/InMemory 双实现+租户隔离+工厂齐备），零新增存储组件
  - **①** Embedding 生成管线 → ✅ ISemanticMemoryService + SemanticMemoryService（集合 semantic-memory、内容寻址 docId 去重）
  - **②** Episodic 记忆写回 → ✅ WorkflowCompleted/RolledBack 双事件 handler（成功经验与失败教训均沉淀，Enabled 开关+异常不伤主流程）
  - **③** 自动 compaction → ✅ 溢出步骤硬截断改为语义召回注入（负数键 [semantic-recall]）；并修复 Summary/Retrieval「建而不用」漂移——AgentCall prompt 新增 History summary / Relevant knowledge 区块
- **完成记录（2026-08-25）**：feature-builder 全栈闭环（分支 `feat/f33-semantic-memory`，基于 f32）。新增测试 7 例（服务3/handler3/prompt渲染1）；全绿 App221/Infra154+6skip/Api35/Arch9，build 0/0，前端零改动。三道质量门 PASS。残留：Compaction 仅接 Sequential 路径；记忆无 TTL 治理（Phase 11）。
- 设计依据：`phases/phase-10-semantic-memory.md` + `docs/agent-harness-blueprint.md` §Phase 10
- 目标：从「文件注入式记忆」升级为语义记忆引擎；`IEmbeddingGenerator` 生成 embedding；episodic 写回；自动 compaction；复用 `IVectorStore`（Pg/InMemory）；租户向量隔离。
- 验收子项：
  - **D3** 向量后端决策（复用 IVectorStore / 引入专用向量库）。
  - **①** Embedding 生成管线（写入向量库）。
  - **②** Episodic 记忆写回（跨会话经验沉淀）。
  - **③** 自动 compaction（超限上下文压缩，替代明文 MaxSummaryTokens 截断；并为 F29 长程 agent 提供上下文压缩）。
- 优先级：P1。

### F34 · 在线评估门禁 + 部署闭环  [P2]  done  🟢低风险（复用 F24 数据集）
- 设计依据：`phases/phase-11-online-eval-gate.md` + `docs/agent-harness-blueprint.md` §Phase 11；设计文档 `features/f34-online-eval-gate.md`（v1 仅验收①，§3 设计+§5 完成记录）
- 验收子项：
  - **①** 在线 eval 门禁 → ✅ `RunEvaluationGateCommand`：阈值解析链（请求显式 > `EvaluationSettings.GateMinPassRate`=0.8）；执行委托 RunEvaluation（一次性克隆=影子隔离零生产写入）；Passed=false 端点返回 **HTTP 422 阻断语义**；空数据集显式守卫恒不通过；审计新增 `AuditActionType.EvaluationGate`
  - **延后项** → CI YAML 接入样例、队列化执行/水平扩展、监控告警聚合、异常回放诊断——均独立排期（与 backlog 延后声明一致）
- **完成记录（2026-08-25）**：feature-builder 全栈闭环（分支 `feat/f34-online-eval-gate`，基于 f33）。端点 `POST /api/v1/evaluation-datasets/{id}/gate/{workflowId}`（Admin/Operator）。新增测试 5 例（超阈值通过+审计/低于阈值阻断/显式覆盖配置/空数据集恒拦/越界抛错）；全绿 App226/Infra154+6skip/Api35/Arch9，build 0/0，前端零改动。三道质量门 PASS。**二期 F29–F34 全部收口。**
- 设计依据：`phases/phase-11-online-eval-gate.md` + `docs/agent-harness-blueprint.md` §Phase 11
- 目标：将 F24 评估数据集接入生产前 / 影子门禁；队列化水平扩展。
- 验收子项（v1）：
  - **①** 在线 eval 门禁（F24 数据集 → 生产前 / 影子评估，纯应用层复用）。
- 延后项（依赖 F30/F32 分布式落点）：队列化部署 / 水平扩展 → 独立排期。
- 优先级：P2。

## 延后项（独立排期，从已 done 史诗中拆出）

> 以下条目均来自 F26/F30/F31/F32/F34 设计文档中显式标注的「延后项」——v1 边界明确排除、依赖未就绪或破坏性过大，需独立 feature 闭环。

### F35 · 多工作空间隔离（Workspace）  [P2]  done  ✅（2026-08-31，分支 `feat/f35-workspace-isolation`；设计文档 features/f35-workspace-isolation.md §6 决策 D1–D5 已锁定 + 质量报告 docs/quality/f35-workspace-isolation-gate.md）🔴高风险（全聚合加 WorkspaceId + query filter + TenantProvider 体系扩展）
- 来源：F26 企业增强 · S1「Workspace v1 不做，独立排期」
- 设计依据：`features/enterprise-enhancements.md` §6 S1
- 目标：创建/切换 workspace；实体按 workspace 隔离；切换后查询仅见当前 workspace 数据。本质是「第二租户维度」——同一租户内再分一层工作空间。
- 核心改造：
  - Domain：所有 `ITenantScoped` 聚合新增 `WorkspaceId`(Guid) 列 + EF 迁移（全仓）；`IWorkspaceRepository` + `Workspace` 聚合（Name/Description/IsDefault）。
  - Application：`ITenantProvider` 扩展 `GetWorkspaceId()`（缺省走 DefaultWorkspace）；`IUnitOfWork` 提交前注入 WorkspaceId；所有 Query Handler 加 `WHERE workspace_id = @wid`。
  - Api：`WorkspacesController`（CRUD + 切换 `POST /{id}/switch` 返回新 JWT claim）；Middleware 从 JWT/Header 解析 WorkspaceId 注入 ITenantProvider。
  - 前端：`WorkspaceSwitcher`（顶栏下拉切换 + 新建）；切换后全站数据刷新。
- 验收子项：
  - 工作流/Agent/对话/知识库按 workspace 隔离：A 建的工作流在 B 不可见。
  - 默认 workspace 自动创建（注册时）；切换后 API 请求带正确 workspace 上下文。
  - 非 Admin 用户只能操作自己所在 workspace（RBAC workspace 级）。
  - EF 迁移覆盖全仓 ITenantScoped 实体；无遗漏。
  - build 0/0 + 全量测试 0 失败 + 前端 tsc 0 + vitest 通过。
- 风险：🔴 破坏性极大——全聚合加列 + query filter 修改 + TenantProvider 体系重构 + 前端全局切换。建议独立分支 feature-builder 全栈闭环。
- **完成记录（2026-08-31）**：feature-builder 全栈实跑落地。决策（用户锁定）：D1=C claim+header 双通道（`IWorkspaceProvider`：claim → header → `WorkspaceDirectory` 租户默认兜底 → 空 fail-closed）/ D2=A 18 聚合全量（AuditLog/ExecutionLog/AgentRunRecord 仅补列）/ D3=B 成员表（非 Admin 仅见默认+已加入，switch 校验成员资格）/ D4=删除守卫（默认 409、非空 409、绝不级联）/ D5=A `useApiState` 单点订阅全站刷新。后端：`Workspace`/`WorkspaceMember` 聚合 + `IWorkspaceScoped` + 18 聚合加列 + `AppDbContext` 组合过滤器与 SaveChanges 注入 + `WorkspaceProvisioner` 幂等供应/回填 + 迁移 `AddWorkspaceIsolation` + `WorkspacesController` 8 端点 + `WorkspaceHeaderGuardMiddleware`（非 Admin 剥离越权头）+ 登录/`/auth/me`/dev-login 携带 workspace claim + API-Key 认证钉到 Key 所属工作空间 + 触发路径 `GetByIdForTriggerAsync`（修复非默认空间工作流被静默跳过的回归）。前端：`WorkspaceSwitcher` + 拦截器注入头 + `appStore.currentWorkspaceId` 持久化 + i18n 对称 + BDD E2E `workspace-switch.feature`。三道质量门全 PASS：ddd-code-reviewer 修 2×P1（header 越权中间件、触发回归）+3 项；结构门 P0-P2=0（2 waiver）；optimizer Round F35-01 0 open（1 修复 + 5 waiver）。验证：build 0/0；App 238 / Infra 158+6skip / Api 35 / Arch 9 / SpecFlow 114/115（唯一失败=master 既有 LLM 用例）/ Integration 5（需 `OPENAI__Key`）；新增 12 handler 测试 + 4 EF 隔离测试；前端 tsc 0 + vitest（2 既有失败豁免）+ vite build。文档同步：CHANGELOG v2.34、BLUEPRINT 平台化清单、appendices/core-aggregates.md（Workspace/WorkspaceMember 聚合）、appendices/api-spec.md（I.11 工作空间 API，资源域 10→11）。已知残留：触发/调度仅落租户默认工作空间、成员列表 N+1、名称唯一大小写依赖 collation、3 个补列实体运行期 WorkspaceId 恒空（D2=A 设计）。

### F36 · Agent 上下文隔离（Blackboard 分区 + 独立对话历史）  [P2]  done  ✅（2026-09-01，分支 `feat/f36-agent-context-isolation` 基于 f35；设计文档 features/f36-agent-context-isolation.md §5 决策 D1–D4 已锁定 + §8 审查修复记录 + 质量报告 docs/quality/f36-agent-context-isolation-gate.md）🟡中风险（Blackboard 语义重构 + per-agent 对话状态）
- 设计文档：`features/f36-agent-context-isolation.md`（已建，§5 决策 D1–D4 待用户锁定；现实修正：Blackboard 实为 Dictionary<string,string>、AgentCallStepExecutor 现从不接触 Conversation）
- 来源：F31 Agent 运行时实体化 · D4「Blackboard 按 agent 分区 / 每 agent 独立对话历史延后」；F32 消息总线 · 明确不做
- 设计依据：`features/f31-agent-runtime.md` §明确不做 + `features/f32-agent-message-bus.md` §明确不做
- 目标：每个 agent 拥有独立的上下文视图——Blackboard 按 agent 分区（agent 只读写自己的分区），对话历史按 agent 隔离（同一工作流内不同 agent 的 Conversation 不互相污染）。
- 核心改造：
  - Blackboard 分区：`Blackboard` 值对象从 `Dictionary<string,object>` 改为 `Dictionary<Guid, Dictionary<string,object>>`（key = agentId）；`WorkflowContext.Blackboard` 粒度对齐。
  - 对话隔离：Conversation 新增 `AgentId`(nullable) 列 + EF 迁移；AgentCallStepExecutor 创建/复用 Conversation 时绑定 AgentId。
  - 编排器：`RunWorkflowBodyAsync` 为每个 agent 调用构造独立 `WorkflowContext`（含分区 Blackboard 视图）。
  - 前端：Conversation 列表支持按 agent 筛选（可选）。
- 验收子项：
  - Agent A 写入 Blackboard 的数据对 Agent B 不可见（分区隔离）。
  - 同一工作流内 Agent A 和 B 的对话历史独立（Conversation.AgentId 生效）。
  - 无 AgentId 的 Conversation（F31 前遗留）回退到全局视图（向后兼容）。
  - build 0/0 + 全量测试 0 失败 + 前端 tsc 0 + vitest 通过。
- 风险：🟡 Blackboard 值对象变更影响 WorkflowContext 全链路；Conversation 加列为最小迁移。依赖 F31/F32 已合入。
- **完成记录（2026-09-01）**：feature-builder 全栈实跑落地（基于 feat/f35-workspace-isolation）。决策（用户锁定）：D1=A 软分区视图（`agent:{agentId}:` 键约定 + GetPartitionView/GetGlobalView；F30/F25/RunningExecution 持久化格式零变更）/ D2=A AgentCallStepExecutor 自动创建/复用 per-agent per-workflow 会话（唯一过滤索引防并发双建；持久化失败 Detach 隔离不阻断）/ D3=A 会话页 agent 筛选+标签 / D4=A 回复显式回写 `agent:{agentId}:output`。现实修正（相对 backlog 原文）：Blackboard 实为 Dictionary<string,string>，AgentCall 原不接触 Conversation。三道质量门全 PASS：reviewer 修 P1（唯一过滤索引）+3×P2；结构门 P0-P2=0（2 waiver）；optimizer 修 P1（Detach）+3×P3，0 open。验证：build 0/0；App 253/Infra 162+6skip/Api 35/Arch 9/SpecFlow 115/116（既有豁免）/Integration 5；新增 18 测试 + SpecFlow 1 场景；前端 tsc 0 + vitest（既有豁免×2）+ vite build。文档同步：CHANGELOG v2.35、BLUEPRINT、appendices（Conversation.AgentId + 会话列表 agentId 参数）、backlog F36 done。已知残留：硬分区列 v2；SetInPartition/GetFromPartition 为预留 API（agent 工具链接入）；截断字面量未抽配置。

### F37 · 队列化执行与水平扩展  [P1]  done  ✅（2026-09-02，分支 `feat/f37-queued-execution` 基于 f36；设计文档 features/f37-queued-execution.md §5 决策 D1–D4 已锁定 + §8 审查修复记录 + 质量报告 docs/quality/f37-queued-execution-gate.md）🔴高风险（分布式消息中间件 + 多 worker 协调；基于 feat/f36-agent-context-isolation 分支）
- 设计文档：`features/f37-queued-execution.md`（已建，§5 决策 D1–D4 待用户锁定；现实校正：现租约 LeaseTtlMinutes=5 非 30s、既有 run 端点为请求内同步契约）
- 来源：F30 执行持久化 · 延后项；F34 评估门禁 · 延后项
- 设计依据：`features/f30-durable-execution.md` + `features/f34-online-eval-gate.md` §延后项
- 目标：将当前进程内 BackgroundService 轮询升级为基于消息队列的分布式任务分发——多 worker 实例可水平消费执行任务，无状态执行引擎横向扩展。复用 F30 租约机制（RunningExecution）防多 worker 重复驱动。
- 核心改造：
  - Infrastructure：`IExecutionQueue` 抽象 + Redis Stream / RabbitMQ 实现（进程内 Channel 替代方案回退保留）；`DistributedLeaseProvider`（Redis `SET NX EX` 替代内存 `SemaphoreSlim`）。
  - Application：`EnqueueWorkflowRunCommand` 替代直接 `IMediator.Send(RunWorkflowNode)`；Worker `BackgroundService` 从队列消费 + `TryAcquireLease` 竞争执行。
  - Api：`DurableExecutionSettings` 新增 `QueueBackend = "InMemory" | "RedisStream" | "RabbitMQ"`；按配置注入。
  - 前端：无改动（对执行发起方透明）。
- 验收子项：
  - 双 worker 同时运行同一工作流，仅一个实际执行（租约互斥）。
  - Worker 崩溃后 30s 租约过期，另一 worker 接管（恢复语义）。
  - Redis 不可用时降级到进程内 Channel（fail-safe）。
  - 评估门禁端点在队列模式下仍正常工作（异步执行 → 同步等待结果）。
  - build 0/0 + 全量测试 0 失败（SkippableFact 覆盖 Redis 不可用场景）。
- 风险：🔴 分布式一致性（租约竞态、消息去重、幂等）+ 运维复杂度（Redis/RabbitMQ 部署）。建议分两阶段：① Redis Stream 最小闭环 ② RabbitMQ 企业级（独立排期）。
- **完成记录（2026-09-02）**：feature-builder 全栈实跑落地。决策（用户锁定）：D1=B 三后端全做 / D2=B run 端点透明「入队+等待」（既有 run/run-existing 契约在 QueueEnabled 下返回 200 完成 / 202 queued / 503 拒投，默认 QueueEnabled=false 直跑零变化）/ D3=A 复用 F30 5min 租约作接管窗口（**校正 backlog 原文「30s」**：现网租约 LeaseTtlMinutes=5，缩至 30s 会改 F30 崩溃恢复窗口，未选）/ D4=A 评估门禁保持同步直跑。**设计偏差（诚实记录）**：① 复用既有 `IDistributedLockProvider`（Redis 实现本就是 SET NX PX 语义）而非新建 `DistributedLeaseProvider`；② 队列投递在 run 命令处理器内透明完成（`QueuedRunSupport.EnqueueAndWaitAsync`），未新增公开 `EnqueueWorkflowRunCommand`；③ Redis/Rabbit 不可用时 run 端点显式 503（不运行时静默切 InMemory，避免多实例脑裂），InMemory 为注册期选定的后端而非运行期降级。落地：`IExecutionQueue`+`ExecutionJob`/`QueueDelivery`/`EnqueueResult`（Application）；三后端 Infrastructure（InMemory Channel 有界 / Redis Stream XADD+XREADGROUP+XAUTOCLAIM+XACK+死信流 / RabbitMQ durable+BasicGet pull+epoch 防跨代 ack+死信队列）；`ExecutionWorker`（BackgroundService，恒注册+QueueEnabled 运行时门控，失败按 Attempt 重投、超限死信、仅接管成功才 ack）；`ExecuteQueuedWorkflowCommand`（消费 scope 复现租户/工作空间 Override、跨租户拒跑、终态重复投递→Duplicate 不重跑、租约冲突→Duplicate、触发投递 FromQueue 防回环）；触发处理器队列模式投递。前端 runWorkflow/runExistingWorkflow union + isQueuedRunResponse 守卫 + queued 提示。三道质量门全 PASS：reviewer 修 P0（重复投递二次执行）+P1×4（轮询 AsNoTracking、死信成败回报防丢任务、Redis 连接泄漏、Rabbit epoch）；结构门 0 open（P3×2 修）；optimizer Round F37-01 0 open（P3×1 修：未知 QueueBackend 静默降级告警；2 waiver）。验证：build 0/0；App 268 / Infra 171+8跳 / Api 37 / Arch 9 / Integration 5 / SpecFlow 115/116（唯一失败=既有豁免）；新增 Application 队列 15 + Infra queue/worker 9 + Api 队列 E2E 2；前端 tsc 0 + vitest（既有豁免×2）+ vite build。文档同步：CHANGELOG v2.36、BLUEPRINT 平台化清单、appendices（api-spec I.3.1 队列模式 / deployment-devops H.4/H.5）、backlog F37 done。遗留：RabbitMQ 真实 broker 投递闭环在 CI services 覆盖（本地跳过）；InMemory 重启丢未 ack 作业（单实例回退设计接受）。

### F38 · CI YAML 接入评估门禁样例  [P2]  done  ✅（2026-09-02，分支 `feat/f38-ci-eval-gate` 基于 f37；交付 ci/eval-gate-github.yml + ci/eval-gate-gitlab.yml + docs/ci-eval-gate-guide.md，设计文档 features/f38-ci-eval-gate.md + 质量报告 docs/quality/f38-ci-eval-gate-gate.md）🟢低风险（文档 + 模板，不触后端代码）
- 来源：F34 评估门禁 · 延后项
- 设计依据：`features/f34-online-eval-gate.md` §延后项
- 目标：提供可直接复制使用的 CI/CD 流水线模板，将评估门禁端点接入 GitHub Actions / GitLab CI，实现「模型/prompt 变更前自动回归，未达阈值阻断合并」。
- 核心改造：
  - `ci/eval-gate-github.yml`：GitHub Actions workflow — 触发 PR + 手动 dispatch → 启动 API → 运行 eval gate → 422 则 `exit 1` 阻断。
  - `ci/eval-gate-gitlab.yml`：GitLab CI template — `rules: merge_request` 触发，同样逻辑。
  - `docs/ci-eval-gate-guide.md`：接入指南（环境变量配置 / 阈值覆盖 / 失败通知 Slack / 故障排查）。
  - 不触后端代码，纯文档 + CI 配置。
- 验收子项：
  - GitHub Actions YAML 语法校验通过（`actionlint` 或 `act` 本地跑）。
  - GitLab CI YAML 语法校验通过（`gitlab-ci-lint` 或 `grep` 关键字）。
  - 接口示例 `curl` 可在本地 QuickStart 模式下跑通（200/422 路径各覆盖）。
  - 指南文档完整：环境变量 / 阈值 / 失败处理 / 故障排查。
- 风险：🟢 纯增量，不触后端。但需与 F34 端点保持接口一致（API schema 变更须同步更新模板）。
- **完成记录（2026-09-02）**：交付 GitHub Actions + GitLab CI 可复制门禁模板（`ci/` 目录，非自动生效）+ 中文接入指南。以 HTTP 码为唯一阻断契约（200 放行 / 422·400·401·403·404·000·其它 exit 1），认证走 httpOnly cookie jar（无 Bearer 反模式），curl 用 `-s -o -w %{http_code}` 而非 `-f`。关键校正：F41 已删 QuickStart→本地验证改「真实实例 / Stub Test 冒烟」；端点越界 minPassRate 实返 500 非 400（服务端无 handler，Program.cs 仅 6 handler）。对抗式评审修 3×高（set -e 抢杀 curl/登录与阈值 JSON 注入/外部 PR ref_name 脚本注入）+2×中，两模板 yaml.safe_load + bash -n 通过、HTTP 码分支 mock 桩冒烟 200/422/000=rc 0/1/1。三道质量门 PASS（结构门/optimizer scope=F38-only，0 open；waiver：真实平台端到端需接入方环境）。

### F39 · 监控告警聚合  [P2]  open  🟡中风险（OpenTelemetry 指标 + 告警规则 + Dashboard 配置）
- 来源：F34 评估门禁 · 延后项
- 设计依据：`features/f34-online-eval-gate.md` §延后项
- 目标：将当前裸 OpenTelemetry `/metrics` 端点升级为可用的可观测性栈——Prometheus 抓取配置 + Grafana Dashboard 模板 + 告警规则（执行失败率、门禁阻断率、队列积压、模型调用延迟），实现「平台运行状态一目了然 + 异常自动通知」。
- 核心改造：
  - `deploy/prometheus.yml`：Prometheus 配置（scrape interval / relabel / 告警规则引用）。
  - `deploy/alert-rules.yml`：告警规则（execution_failure_rate > 10% / eval_gate_block_rate > 5% / queue_depth > 100 / model_latency_p99 > 30s）。
  - `deploy/grafana/dashboards/agent-platform.json`：Grafana Dashboard JSON（执行量趋势 / 成功率 / 门禁通过率 / 延迟分布 / 队列深度）。
  - `docs/observability-guide.md`：部署指南（Docker Compose 一键起 Prometheus+Grafana / 告警对接 Slack/PagerDuty / 自定义 Dashboard）。
  - 不触后端代码（OpenTelemetry SDK 已在 F5/F20 引入），纯运维配置。
- 验收子项：
  - Prometheus 配置语法校验通过（`promtool check config` 或等价校验）。
  - Grafana Dashboard JSON 可导入且面板无报错。
  - 告警规则阈值合理（参考实际测试数据：App 226 测试 / Infra 154 通过率）。
  - 指南文档完整：一键部署 / 告警对接 / 自定义。
- 风险：🟡 Grafana Dashboard JSON 维护成本（版本升级可能断面板）；缓解：文档注明版本要求。

### F40 · 异常回放诊断入口  [P2]  open  🟡中风险（执行日志回放引擎 + 前端诊断视图）
- 来源：F34 评估门禁 · 延后项
- 设计依据：`features/f34-online-eval-gate.md` §延后项
- 目标：从执行日志重建失败工作流的异常路径——定位失败节点、回放输入输出、展示上下文快照（Blackboard/变量/模型响应），辅助快速定位根因。复用 F24 Trace + F25 调试器能力。
- 核心改造：
  - Application：`ReplayExecutionCommand`（接收 ExecutionLogId）—— 从 `ExecutionLog.Entries` 重建执行路径，标记失败节点，返回 `ReplayReport`（路径序列 + 每节点 IO/耗时/错误信息 + Blackboard 快照）。
  - Api：`POST /api/v1/execution-logs/{id}/replay`（`[Authorize]`，只读）。
  - 前端：`ExecutionLogDetailPage` 新增「回放诊断」Tab（时序图展示失败路径 + 节点展开查看输入输出 + Blackboard 变量表）。
- 验收子项：
  - 传入失败执行日志 ID，返回完整回放报告（失败节点高亮 + 前后上下文）。
  - 传入成功执行日志 ID，返回完整路径无失败标记。
  - 传入不存在 ID → 404。
  - 前端回放视图清晰展示失败链路，可折叠/展开每节点详情。
  - build 0/0 + 全量测试 0 失败 + 前端 tsc 0 + vitest 通过。
- 风险：🟡 回放依赖 ExecutionLog Entries 数据完整性（F20 Trace 节点级数据采集）；历史日志可能缺少 TokensIn/TokensOut/NodeType 列（F24 前迁移的数据）——需降级兼容。

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
