# 变更日志

## v2.7 (2026-07-28)

### F14 · 供应商模型发现（填 Key + Base URL 后拉取可访问模型清单）完成（feature-builder 全栈实跑，🔴高风险）

用户在「我的凭据」/Agent 配置页填 API Key + Base URL + 选 Provider 后，点「拉取模型」即可从该 provider 账户（OpenAI 兼容 `GET /v1/models`）拉回所有可访问模型，以下拉供选择，免去手填模型名易错的问题。

**核心改动：**
- **后端发现服务**：新增 `IProviderModelDiscovery`（Application.Abstractions 接口）+ `ProviderModelInfo` record + `ProviderModelDiscoveryException`（领域友好异常，携带可直接回传客户端的 400 中文原因，绝不泄露密钥）+ `ProviderModelDiscovery`（Infrastructure.Models，真实 `HttpClient` 出站，复用 `SerpApiSearchProvider` 的 `IHttpClientFactory` 模式，无 stub）。
- **端点**：`TenantCredentialsController` 新增 `POST discover-models`（RBAC `Admin,Operator`，只读探测、无落库、无密钥出 API 体）；`DiscoverModelsRequest`（provider / baseUrl / apiKey）。默认 base：OpenAI/DeepSeek 内置、Custom/VLLM 须显式填。
- **DI 注册**：`IProviderModelDiscovery` 注册 Scoped 单实现，控制器注入消费；无 EF 迁移。
- **前端契约**：`types/index.ts` 加 `ProviderModelInfo`、`api.ts` 加 `discoverProviderModels`；`CredentialForm` 模型类 `Model Name` 改 `AutoComplete`（允许自定义）+「拉取模型」按钮（loading / 错误提示 / edit 模式留空 Key 禁用并用 `Tooltip` 提示先填 Key）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f14-model-discovery`
- 审查修复 P1：`ProviderModelDiscovery` 原 `response.Content.ReadAsStringAsync` 位于 `try` 之外，若 15s 超时发生在读取响应体阶段会抛未捕获 `OperationCanceledException` → 500，已移入 `try` 并用 `using var response` 全程受请求级超时保护，超时统一映射为友好 400
- 审查修复 P2：`CredentialForm`「拉取模型」disabled `Button` 用 `title` 提示禁用原因但 antd v5 吞掉 hover 导致提示不可见，已用 `Tooltip` 包裹使其 hover 显示（满足 D1「按钮提示先填 Key」）
- `dotnet test src/AgentPlatform.sln` **255 passed / 0 failed**（含 F14 新增 11 例 ProviderModelDiscovery 单测，覆盖 URL/解析/401/404/空 data/缺 owned_by 等）；前端 `tsc --noEmit` **0 error** + `vite build` 通过
- 模型一致性：后端 camelCase 序列化 `{id, ownedBy}`、前端对应 `{id, ownedBy}`

**已知残留（非阻断）：**
- e2e 浏览器联动（Playwright/Edge）本沙箱未跑，单测已覆盖真实 HTTP 探测路径（StubHttpMessageHandler 验证 GET+Bearer+URL）
- SSRF 域名白名单不在本范围（D4），Admin 专用可接受

**分支：** `feat/f14-model-discovery`

## v2.6 (2026-07-27)

### F13 · 多租户凭据配置（模型 + 搜索，BYO-Key + 平台内置）完成（feature-builder 全栈实跑，🔴高风险）

补齐平台多租户化的最后一环——外部 API 凭据层租户隔离（模型 LLM key + Research 用 SerpApi 搜索 key 同构处理）。

**核心改动：**
- **聚合与落库加密**：新增 `TenantCredentialSetting` 聚合（`ITenantScoped` → `HasQueryFilter` 租户隔离；`Id` 显式 `ValueGeneratedNever`）+ `CredentialCategory` 枚举（Model/Search）+ `ITenantCredentialSettingRepository` + EF 迁移 `AddTenantCredentialSetting`。密钥复用 `IApiKeyEncryptionService`（AES-256-GCM），落库仅存密文 `EncryptedApiKey` + `ApiKeyPrefix`，明文不入 DB/不出 API/不进日志。
- **per-tenant 解析链路**：新增 `ITenantCredentialResolver`（按 `tenantId+category` 解析 + `IMemoryCache` 缓存密文实体 + `PUT` 即时失效）、`ITenantModelClientResolver`（解密后 `SemanticKernelModelClient.CreateForTenant` 构建租户模型客户端）、`IPlatformModelProvider`（运营方 `RouterSettings.Candidates` 平台模型）。`ModelRouter` 改造为合并平台 ∪ 租户候选；`SerpApiSearchProvider` 改为运行时按租户解析 key（BYO key 绕过平台配额，无则回退平台默认，均无则明确提示配 key）。
- **配额（B 防滥用）**：`ICostController` 扩展为租户键控（`PerTenantDailyBudget` 模型 / `PerTenantDailySearchQuota` 搜索）；BYO-Key 不受限。
- **端点与前端**：`TenantCredentialsController`（`GET/PUT /api/v1/tenant/credentials?category=Model|Search`，RBAC `Admin,Operator`，GET 返回掩码 `••••`+prefix，未配置 204）+ `PlatformModelsController`（`GET /api/v1/models`，平台 ∪ 租户 BYO，仅暴露标识不含密钥）。前端 Agent 配置页内嵌 `Tabs: 模型 + 搜索` 凭据配置（`Input.Password` 掩码 + provider Select + 保存）；`types/index.ts`+`api.ts` 补齐。
- **S4 收尾（模型下拉接线）**：Agent 创建页（Admin 专属「+ 新建 Agent」Modal，含角色 + 模型下拉，模型选项来自 `GET /api/v1/models`，选中的 `modelId`→`ModelName`、provider→`ModelProvider`）与会话详情页（顶栏「选择模型」下拉，分组「平台模型 / 我的模型」，选中值经 `sendMessage(model=modelId)` 透传为 `PreferredModel` 路由）均已接 `GET /api/v1/models`；`appStore` 新增 `userRole` 用于 Admin 按钮门控。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f13-multi-tenant-credentials`
- 审查修复 P0：`TenantCredentialsController.Put` 原直接写仓储但未提交 `IUnitOfWork.SaveChangesAsync`（本控制器不走 MediatR 命令、无 `UnitOfWorkBehavior` 自动提交），导致凭据永不落库——已注入 `IUnitOfWork` 显式提交，行为与命令处理器一致；新增 EF 集成测试锁定落库 + 租户隔离 + upsert 不重复行
- `dotnet test src/AgentPlatform.sln` **244 passed / 0 failed**（含 F13 新增 EF 集成测试、resolver/search BYO 单测）；前端 `tsc --noEmit` **0 error**
- 模型一致性：后端 camelCase 序列化、枚举 int，前端 `CredentialCategory` 常量对象一一对应

**已知残留（非阻断）：**
- ~~S4 模型下拉接 `GET /api/v1/models` 后端已就绪（返回 platform ∪ BYO），Agent/会话创建页模型下拉接线为后续小步~~ → **已完成**（见上方「S4 收尾」）。
- `appsettings.json` 因严格 JSON 不容注释，配额语义改在 `features/model-config.md` §3.6 文档化

**分支：** `feat/f13-multi-tenant-credentials`

## v2.5 (2026-07-24)

### F6 · Research Agent 联网多步调研完成（feature-builder 全栈实跑，🔴高风险）

把「开放问题 → 多步联网检索 → 结构化报告」做成一等能力（Research Agent）。原蓝图阶段四 TODO「Research Agent」落地。

**核心改动：**
- **真实联网检索**：新增 `ISearchProvider` + `SerpApiSearchProvider`，对 `serpapi.com/search.json` 发起**真实 GET** 并解析 `organic_results`（标题/URL/摘要）；缺 key / 非 2xx / 超时 / 传输错误 → `Success=false` + 真实 `ErrorMessage`，**绝不伪造成功**。密钥走 `SearchSettings` / 环境变量 `Search__SerpApiKey`，**不落库**（不复用 `ToolDefinition.EndpointUrl`）
- **多步链真实串联**：`ResearchCommand` + `ResearchCommandHandler`（注入 `IModelClient` / `ISearchProvider` / `ITokenCounter` / `IOptions<StateMachineSettings>` / `IOptions<SearchSettings>`）按 `plan → search×N → synthesize` 自驱循环；`Sources` 按 URL 去重累积；多轮发现超 `MaxSummaryTokens`(8000) 预算截断。LLM 规划/综合均经注入 `IModelClient`（生产真实 SemanticKernel，测试 stub）
- **SSE 流式端点**：`ResearchController`（`POST /api/v1/research`，`[Authorize]` 全认证租户用户）以 `text/event-stream` 流式写出 `ResearchProgressEvent`（`Plan → SearchStart/SearchDone×N → Synthesize → Report`，异常为 `Error`+空 `Report`），终端 `event: done` 收尾；序列化 camelCase、事件 `Type` 整型枚举（0–5）
- **配置**：新增 `SearchSettings`（`Application.Abstractions`）+ `appsettings.json` 的 `Search` 节（`Provider`/`SerpApiKey`/`BaseUrl`/`TimeoutSeconds`/`DefaultMaxResults`）；`DI` 按 `Provider` 选择实现（未知值启动报错）+ `AddHttpClient()`
- **前端**：新增 `ResearchPage`（提问 + 实时 Timeline 进度 + 结构化报告渲染：来源卡片 / 答案 / 分节）、`types/index.ts` 的 Research 类型、`api.ts` 的 `runResearch`（fetch + `credentials:'include'` 逐帧解析 SSE）、`App.tsx` 路由 `/research`、`AppLayout.tsx` 菜单项

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f6-research-agent`
- 已核对**真实副作用验收**：`SerpApiSearchProviderTests`（StubHttpMessageHandler 模拟 SerpAPI）覆盖真实 GET 构造 + `organic_results` 解析 + 缺 key/非 2xx/超时/传输错误；`ResearchCommandHandlerTests` 覆盖搜索调用 N 次 / `Sources` 去重 / `Sections` 非空 / 计划·综合失败精准回打
- `dotnet test src/AgentPlatform.sln` **238 passed / 0 failed**（含 F6 新增 8 例）；前端 `tsc --noEmit` **0 error** + `vite build` 通过
- 模型一致性：后端 camelCase、事件 `Type` 整型枚举，前端 `ResearchEventTypeValue` 常量对象一一对应

**已知残留（非阻断）：**
- `SerpApiKey` 为空时各查询失败但报告仍基于已规划内容生成（优雅降级）
- 真实 SerpApi 端到端需生产密钥（单测用 mock transport 覆盖真实 HTTP 路径）
- 报告正文体为 Markdown 文本前端以 `pre-wrap` 渲染（未引 `react-markdown` 依赖，结构化字段 `Sources`/`Answer`/`Sections` 已拆分）

**分支：** `feat/f6-research-agent`

## v2.4 (2026-07-24)

### F5 · 行动层落地（Agent 真正能做事）完成（feature-builder 全栈实跑，🔴高风险）

把原先**空心**的执行层变成**真实副作用**：调工具、跑代码均产生真实外部效果，而非伪造成功。

**核心改动：**
- **A1 原生工具真实 HTTP**：`NativeToolExecutor` 从「返回假成功」改为对 `ToolDefinition.EndpointUrl` 发起真实 HTTP 调用；方法解析（默认 POST、无参走 GET、显式 `httpMethod` 覆盖）、2xx→成功回体、非 2xx→精准回打真实状态、超时→`工具调用超时`。符合 Phase 6 critic 范式（失败精准回打）
- **A2 代码沙箱真实进程**：新增 `ProcessCodeSandbox`（`System.Diagnostics.Process` 拉起 python / node 真实运行），捕获真实 stdout / stderr / ExitCode / 超时杀进程，替代原伪造成功的 `DockerCodeSandbox`（后者改为显式抛异常，消除静默假成功）。Docker 在本沙箱不可用，用户确认进程沙箱为默认真实路径
- **A3 Tool / Code 工作流节点**：新增 `ToolStepExecutor` / `CodeStepExecutor`，注册为 `StepType.Tool=6` / `Code=7` 节点执行器，经既有 `ResolveExecutor`（`HandlesType` 匹配）真实路由；前端 DAG 画布补 Tool / Code 节点（调色板 / 图标 / 配置面板 / node-type 映射）
- **配置**：新增 `SandboxSettings`（`Application.Abstractions`）+ `appsettings.json` 的 `Sandbox` 节（`Provider` 默认 `Process`、`TimeoutSeconds`、`HttpTimeoutSeconds`、`AllowedLanguages` 白名单、`NetworkEnabled` 默认 `false`、`MaxOutputBytes`、`InterpreterPaths`）；`DI` 条件注册 `ICodeSandbox`（Docker / Process）+ `AddHttpClient()`

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f5-action-layer`
- 已核对 A1 / A2 / A3 **真实副作用验收**：新增 13 例单测真实走 HTTP `SendAsync` + 真实 python/node 子进程（print→stdout、raise→stderr、sleep(30) 超时杀、ruby 白名单拒绝）
- `dotnet test src/AgentPlatform.sln` **230 passed / 0 failed**（含 F5 新增 13 例）；`tsc --noEmit` **0 error**

**已知残留（非阻断，waiver target Phase 6）：**
- 真实 Docker 容器隔离（需 Docker.DotNet + 守护进程）；Skill / MCP 执行器占位（设计为 A1 仅要求 NativeToolExecutor 真实化）
- 进程模式无法在 OS 层强制禁网，以 `NetworkEnabled=false` + 语言白名单 + 超时杀 + 输出截断缓解
- 含 Tool/Code 节点的全链路 e2e 需后端 + Web 实例，本沙箱未跑（单元层已覆盖真实执行路径）

**分支：** `feat/f5-action-layer`

## v2.3 (2026-07-24)

### F4 · 前端工程化完成（feature-builder 全栈实跑）

补齐前端工程化短板：拆包、去静态 message、清死代码、补 a11y、补单测。

**核心改动：**
- **O6 路由级拆包**：`App.tsx` 全部页面改 `React.lazy` + `<Suspense>`；`vite.config.ts` 的 `manualChunks` 函数式拆 `react-vendor` / `antd` / `xyflow` 三块供应商分包。首屏主包由 ~1.38MB 降至 `index` 9KB，供应商与页面按需并行加载（build 产物已验证）
- **O9 静态 `message` → `App.useApp()`**：LoginPage / WorkflowCanvasPage / ApiKeysPage / ConversationDetailPage / ConversationsPage / KnowledgeBaseDetailPage / KnowledgeBasesPage 共 7 个页面（WorkflowsPage/AppLayout 已于 F3 完成），消除 antd 静态 message 的 context 丢失告警；grep 全仓 0 处静态 `message.`
- **O10 死代码清理**：`appStore` 移除从未被读取的死字段 `userRole`（接口 + 5 处赋值）；编辑器节点编辑/删除（NodeConfigPanel + 删除按钮 + Delete 键）经核实已满足，不重复实现
- **O14 可访问性**：侧栏折叠按钮、会话搜索框、聊天输入框补 `aria-label`
- **O7 关键页单测**：新增 `appStore` 鉴权态迁移（5 例）、`useApiState` 加载/错误/retry/卸载安全（4 例）、`LoginPage`（3 例）、`NotFoundPage`（1 例），覆盖鉴权态 / 异步错误态 / 登录 / 404

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f4-frontend-engineering`
- **前端四道闸门 PASS**：`node scripts/qa.mjs`（typecheck / lint / build / unit）全绿
- e2e 闸门因沙箱无后端实例未执行，留待有后端环境补跑 `node scripts/qa.mjs --e2e`

**分支：** `feat/f4-frontend-engineering`

## v2.2 (2026-07-24)

### F3 · 页面交互打磨完成（feature-builder 全栈实跑）

列表/筛选/表单交互打磨 + 后端 `/conversations` 服务端筛选补完。

**核心修复：**
- **B10 状态色块错乱（根因）**：后端 `Program.cs` 未注册 `JsonStringEnumConverter`，枚举按**整数**序列化；原前端用小写字符串做 color map 的 key 永远 miss。新增 `src/status.ts` 单一事实源（`mapWorkflowStatus` / `WORKFLOW_STATUS_FILTER_OPTIONS` 整数枚举值 / `CONVERSATION_STATUS_META`），ExecutionLogs + Workflows 状态 Tag 与筛选下拉统一改用，色块正确且不再裸传小写字面量
- **B9** AgentConfigurations「View」按钮打开 Drawer 展示 `yamlContent`（等宽、可滚动，无新依赖）
- **B11** Workflows「快速运行」空名 → `message.warning` 且保持弹窗；`runWorkflow` 包 try/catch → 失败 `message.error`，成功才关弹窗并刷新
- **Conversations** 新增搜索框（ID/Agent/工作流/知识库）+ 状态筛选；由**客户端**改为**服务端**——后端 `GetConversationsQuery` 补 `status`+`q`，`ConversationsController` 绑定 `[FromQuery]`，前端 `getConversations` 改对象参数传 `status`/`q`
- **O12** ExecutionLogs / Workflows / AgentConfigurations 接入服务端分页（`total` + `onChange` → `skip/take`），与后端 `totalCount` 一致
- **O13** 四个列表 getter 支持 `AbortSignal`，各页 `useEffect` 内 `AbortController` 卸载时 `abort()`，杜绝 setState-after-unmount

**顺带修复的预存路由 bug（阻塞 e2e / 页面可用性）：**
- `AgentConfigurations` / `ExecutionLogs` / `AgentRoles` 三 controller 原用 `[Route("api/v1/[controller]")]`，ASP.NET 把类名展开为**无连字符**（`agentconfigurations` / `executionlogs` / `agentroles`），而前端一贯用连字符路径（`agent-configurations` 等）→ 404。改为显式连字符路由并同步修正 `EndpointContractTests` 断言。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f3-page-polish`
- **e2e 闸门 PASS**：`npx playwright test` **14 passed / 0 failed**（前端 cookie 鉴权规格 + 新增 `e2e/page-polish.spec.ts`）
- **后端单测 PASS**：`dotnet test src/AgentPlatform.sln` **214 passed / 0 failed**

**分支：** `feat/f3-page-polish`

## v2.1 (2026-07-24)

### F2 · 登录与鉴权态一致性完成（feature-builder 全栈实跑）

把「前端 localStorage + Bearer」的脆弱鉴权态改为 **httpOnly + SameSite Cookie 承载 JWT**，并把登录密码从「形同虚设」改为 **PBKDF2 真实校验**（`dotnet test` 214/0，`node scripts/qa.mjs` 4/4）。

**后端：**
- 新增 `User` 聚合（`ITenantScoped` + `IAggregateRoot`）+ EF 迁移 `AddUserAggregate` + `UserConfiguration`（租户内邮箱唯一索引）+ `UserRepository`；`DatabaseInitializer` 幂等种子默认用户 `admin@acme.io / Admin@123456`（仅 Development/QuickStart 环境）
- `IPasswordHasher` + `Pbkdf2PasswordHasher`：PBKDF2-SHA256，10 万迭代，16B 盐，固定时间比对；格式 `$pbkdf2$<iter>$<saltB64>$<hashB64>`（零新依赖，用 `Rfc2898DeriveBytes`）
- `IJwtTokenService` / `JwtTokenService` 从 `DevLoginEndpoint` 抽取 token 发行逻辑
- `AuthEndpoints`：`POST /api/v1/auth/login`（验密→设 `ap_access_token` cookie：HttpOnly + SameSite=Lax + Secure=IsHttps + MaxAge=1h，返回 `{user}`）、`GET /api/v1/auth/me`（从 cookie 解析身份）、`POST /api/v1/auth/logout`（清 cookie）
- `AuthConfiguration` Smart 策略 `OnMessageReceived` 从 cookie 读 JWT；CORS 去 `AllowAnyOrigin` → `WithOrigins(Cors:AllowedOrigins)` + `AllowCredentials`

**前端：**
- `api.ts`：`axios.create({ withCredentials: true })`，移除 Bearer 注入与 localStorage；响应拦截器 401 派发 `auth:unauthorized` 事件
- `appStore` 去 localStorage，新增 `authBootstrapped` / `isDemo` / `bootstrapAuth()` / `loginReal()` / `loginDemo()` / `logout()`
- `LoginPage` 密码框 + 真实登录 + 「使用本地演示会话」；`ProtectedRoute` 等 bootstrap；`App` 监听 `auth:unauthorized` → 非 demo 跳 `/login`；SSE `fetch` / `EventSource` 改 `credentials:'include'`

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；新增 `AuthEndpointsTests` 5 例 + `Pbkdf2PasswordHasherTests` 5 例
- 分支 `feat/f2-login-auth-state`（commit `19af124` + `4af3fe9`），`.quality-gate.json` 推进 `f2-login-auth-state`

**已知残留（非阻断）：** 多租户登录按默认租户查用户（P2 waiver，目标后续「多租户登录」feature）；`Security:JwtSecretKey` 含 dev 兜底值（生产须环境变量覆盖）；种子默认密码生产须改

## v2.0 (2026-07-21)

### Phase 5 安全加固完成（launch-blocking）

把蓝图声称"第一优先级"、实际整层缺失的安全底座真实接线并通过二次评审闭环（`dotnet build` 0/0，`dotnet test` 103/103）。

**核心交付：**

- **认证双方案并存**：JWT Bearer + API-Key，用 `Smart` policy scheme（`ForwardDefaultSelector` 按请求头分发）作为默认方案；`ApiKeyAuthenticationHandler` 遵守 `NoResult()`（不适用）/ `Fail()`（无效）语义
- **真实多租户**：`TenantProvider` 从硬编码默认租户改为 per-request（Scoped）从 claim 解析 `tenant_id`，激活 `AppDbContext` 早已建好的 `HasQueryFilter` 隔离
- **RBAC**：`GetRoles` 从凭证取真实角色（Admin/Operator/Viewer），非恒 Admin
- **API Key 加密 + 生命周期**：`AesGcmEncryptor`（AES-256-GCM）+ `ApiKeyEncryptionService`；`ApiKey` 聚合 DB-backed（密文列）+ `IApiKeyRepository`；`Rotate/Revoke` + `ApiKeyExpiryJob`（每 6h 扫描过期）
- **提示注入防护**：`PromptInjectionMiddleware` + `PromptInjectionService`，正则收窄 + 负向测试
- **审计日志**：`AuditLog` 聚合 + `AuditActionType`，覆盖业务 4 handler + Key 三点位（KeyUsed/KeyRotation/KeyRevoked）
- **限流**：ASP.NET Core RateLimiter 按租户/Key 维度（`Security:RateLimitPerMinute`）

### 收尾排障（三个"编译过、运行炸"的坑）

- **认证无默认方案**：`AddAuthentication()` 空配置 → 访问 `[Authorize]` 抛 `No DefaultChallengeScheme found`。修复：加 `Smart` policy scheme
- **Swagger 无模拟登录**：缺 `AddSecurityDefinition` → 无 Authorize 按钮。修复：Swagger + Scalar 补 `Bearer` 定义；新增 `POST /api/dev/login`（`DevLoginEnabled` 门控、默认 false、返回裸 token）
- **`no such table: AgentConfigurations`**：`DatabaseInitializer` 用 `EnsureCreatedAsync()` 与 EF 迁移混用 → 旧 DB 缺 `AgentConfigurations`/`ApiKeys`/`AuditLogs`。修复：改用 `MigrateAsync()`；补落迁移 `Phase5ApiKeyIndex`；删旧 DB 迁移重建

### EF Core 迁移
- `Phase5ApiKeyStorage`：新增 `ApiKeys` + `AuditLogs` 表
- `Phase5ApiKeyIndex`：`ApiKeys` 索引由 `IX_ApiKeys_ExpiresAt` 改为 `IX_ApiKeys_IsActive_RevokedAt_ExpiresAt`

### 文档
- 新增学习笔记 [`docs/learning/10-phase5-security-learnings.md`](./docs/learning/10-phase5-security-learnings.md)（7 个安全知识点 + 3 个排障实录）
- `06-common-pitfalls.md` 扩充至 31 坑（新增认证/Swagger/迁移 5 坑）；同步导读、演进、决策日志、速记卡
- README 阶段路线 Phase 5 标记完成

> 说明：CHANGELOG 从 v1.6 直接跳到 v2.0——Phase 3（平台化）/Phase 4（知识接地加固）的详细条目见 `phases/phase-3-platformization.md`、`phases/phase-4-grounding.md` 与对应学习笔记。

## v1.6 (2026-07-15)

### Phase 2 多智能体工作流完成

**核心交付（9 个模块，70+ 源文件）：**

- **AgentType 值对象迁移**：`AgentRole` 枚举 → `AgentType` record 值对象，EF Core `OwnsOne` 映射，全套向后兼容
- **自研状态机引擎**：`WorkflowStateMachineEngine`，支持分支/重试（最多 3 次）/回滚，通过 `StateMachineSettings` 配置超时与重试策略
- **Redis 短期记忆**：`RedisShortTermMemory` 实现 `IShortTermMemory`，`IConnectionMultiplexer` Singleton 注册，连接失败降级到内存
- **AutoGen 多 Agent 协作**：6 种角色（需求→产品→架构→开发→测试→文档），`AutoGenAgentOrchestrator` 顺序管线编排
- **ExecutionLog 持久化**：`ExecutionLog` 聚合根 + `IExecutionLogRepository`，5 个 MediatR 领域事件驱动日志写入
- **可插拔数据库架构**：条件编译 `USE_SQLITE`/`USE_POSTGRESQL`，`DatabaseInitializer` 自动初始化和种子数据
- **CQRS 查询端点**：`GetAgents`、`GetConversations`、`GetExecutionLogs` 通过 MediatR Query/Handler
- **自定义 Agent 角色 CRUD**：`AgentRoleDefinition` 聚合根，`AgentRolesController` 完整 REST 端点
- **端到端集成**：完整管线需求 → 6 Agent → 输出，状态机持久化 + 恢复，ExecutionLog 全链路记录

### 新增 SpecFlow BDD 验收（5 个 .feature 文件）
- `AgentTypeMigration.feature`（3 场景）
- `WorkflowStateMachine.feature`（6 场景：正常流/重试/回滚/分支/并发/恢复）
- `MultiAgentPipeline.feature`（4+ 场景：完整管线/缺失 Agent/自定义角色/最大轮次）
- `ExecutionLog.feature`（5 场景：查询/过滤/分页）
- `CustomAgentRole.feature`（5 场景：CRUD + 验证）

### 新增配置类（6 个，全部通过 IOptions）
- `AutoGenSettings` — Agent 模型分配、最大轮次、终止条件
- `RedisSettings` — 连接字符串、过期秒数、Key 前缀
- `StateMachineSettings` — 最大重试、回滚超时、步骤超时
- `ExecutionLogSettings` — 保留天数、批量写入阈值、SSE 开关

### EF Core 迁移
- `Phase2MultiAgent` 迁移：8 张表（AgentType `OwnsOne`, ExecutionLog+Entries, WorkflowStep 等）
- 迁移可向前兼容（不破坏 Phase 1 已有表）

### 质量门审计
- **初次审计**（2026-07-15）：Gate Status PASS — 修复 P1×1（`IDatabaseInitializer` 移到 Application.Abstractions）、P3×3（sealed 修饰符、重复 Swagger 调用）
- **回归审计**（2026-07-17）：Gate Status PASS — 全 16 类审计通过，修复 P3×1（`AgentRoleDefinition` null! 注释）
- 最终验证：`dotnet build` 0 警告 0 错误，`dotnet test` 63/63 全部通过

### 蓝图同步
- `AGENT_PLATFORM_BLUEPRINT.md` Phase 2 任务清单已全部勾选
- `phases/phase-2-multi-agent-checklist.md` 完成审计记录更新

## v1.5 (2026-07-13)

### 变更
- **移除 Swagger/Scalar 环境限制**：`Program.cs` 取消 `if (app.Environment.IsDevelopment())` 条件，所有环境默认启用 API 文档
- **默认打开 Swagger UI**：`launchSettings.json` 3 个 profile 的 `launchUrl` 从 `openapi/v1.json` 改为 `swagger`
- **anchored-summary 同步**：移除 4 处 "Scalar (Development only)" 引用，更新为"所有环境默认启用"
- **phase-3-platformization 同步**：Swagger/Scalar 集成相关学习目标和任务项已勾选完成
- **phase-1-baseline-mvp 同步**：M1 修复记录补充"后续进一步移除环境限制"
- **AGENT_PLATFORM_BLUEPRINT 同步**：更新至 v1.5，追加修改日志
- **CHANGELOG 完善**：补充 v1.2~v1.5 缺失条目

## v1.4 (2026-07-10)

### Phase 1 全部代码优化完成

- UnitOfWorkBehavior 事件顺序修复（先分发领域事件，再 SaveChangesAsync）
- ConversationsController → MediatR Command/Handler（`CreateConversationCommand`、`SendMessageCommand`）
- CostController 接口抽象（`ICostController`，ModelRouter 通过接口引用）
- Db 凭据安全化（移除硬编码连接字符串，改为必填配置）
- Scalar 环境限制放宽（从 `IsDevelopment()` 改为 `IsProduction()` 才屏蔽）
- Conversation/Message UpdatedAt 修复（`set;` → `private set;`）
- 空守卫补全（7 个领域方法参数加 `ArgumentException.ThrowIfNullOrWhiteSpace`）
- using 清理（移除未使用的 import）

### 蓝图同步 (v1.4)
- QuickStart URL/cURL 修正（`--launch-profile QuickStart` + 正确 cURL 示例）
- Phase 1 清单已勾选
- 目录树补充 Conversations/ 和 SpecFlowTests
- 缺失 Abstractions 补全（`IResiliencePipelineProvider`、`TenantSettings` 等）
- Workflow 项目标记 Phase 2 骨架
- 删除 Aspirational Serilog 配置，代以 ILogger 现状描述
- 补充 OpenAI:Key / 环境变量文档

## v1.3 (2026-07-09)

### 补充 DDD 铁律
- 仓储 DI 注册说明（`IAgentRepository` 在 Domain 定义接口，Infrastructure 实现并注册）
- 实现类位置约束（所有实现必须放在 Infrastructure 层，不可在 Application 层）
- 接口定义位置约束（抽象接口定义在 `Application.Abstractions`，不可在 Infrastructure 层定义）

## v1.2 (2026-07-09)

### 版本锁定与约定完善
- 锁定 SK 版本为 1.30.0（技术栈选型表标注）
- 明确 MediatR v12+ DI 指南（`AddMediatR` 内置注册，无需独立包）
- 修正 QuickStart 启动命令（`--configuration` → `--launch-profile`）
- 添加测试项目位置约定（`src/` 目录下）
- 补充 EF Core 聚合根映射说明（附录 A.5）

## v1.1 (2026-07-01)

### 新增
- **Section 八：监控与运维**（补齐之前缺失的编号）—— 8.1~8.6 覆盖指标定义、埋点策略、Dashboard 设计、告警规则、日志采集、P0 性能目标
- **附录 C.8：Agent 角色可扩展性**—— 从 `AgentRole` 枚举到 `AgentType` record 值对象的改造方案，含现状分析、预留扩展空间、前后代码对比、联动改动清单、前端 UX 图
- **附录 G.8：前端架构详述**—— zustand 状态管理、TanStack Query API 层、React Router 路由、CanAccess 权限组件、React Flow 编辑器集成、完整 `src/` 目录结构
- **附录 H：部署与 DevOps**—— Docker Compose 开发环境、生产部署架构、CI/CD 流水线、环境配置管理、扩容策略、前端发布
- **附录 I：API 接口规范**—— 7 个资源域（认证/工作流/Agent/模型/对话/监控/管理），含 JSON 示例和 SSE 流式协议
- **Section 十一：编码约定**—— 命名规范表、Git 工作流、AI 编码约束提示词模板、测试约定、文档维护流程
- **Section 12：失败场景示例**—— 模型降级全链路日志输出、SQL 状态查询、人工恢复步骤
- **1.1 非功能目标**—— 可用性 99.9%、数据持久性 99.999%、并发租户 ≥ 100 等 P0 指标
- **10.1 5 分钟快速开始**—— SQLite + Stub 模式，无需 Docker 即可本地运行

### 重构
- **附录拆分**：9 个附录（3081 行）从主文档拆分为 `appendices/` 下独立 `.md` 文件
- **主文档瘦身**：从 ~3656 行减至 ~660 行，AI 加载速度提升 5x
- **9 个附录全部添加** `[← 返回主文档]` 链接
- **ToC 改为外部链接**：附录指向 `./appendices/xxx.md`

### 修复
- 章节编号跳号（缺八）已补齐
- C.8 AgentType 改造成本已同步到阶段二/三/四任务清单
- 项目定位更新为"6 种预置角色 + 自定义 AgentType"
- 8.6 段落末尾孤立 ``` 代码围栏已删除
- 附录 H `---` 前锚点标签丢失已恢复

### 元数据
- 主文档顶部添加版本号、最后更新日期、修改日志
- 附录 C 和 G 的子节（C.1~C.8 / G.1~G.8）添加 `<a name>` 锚点
- 附录索引添加阅读路线图（初次通读/按需查阅/常见场景）

---

## v1.0 (基线)

完整蓝图初版，包含：

- 项目定位、技术栈选型对照表（Python vs C# 匹配度）
- DDD 分层架构目录脚手架（6 个项目）
- BDD/TDD 工程化（SpecFlow + xUnit）
- 阶段一~四任务清单（基础 MVP → 多Agent → 平台化 → 前沿特性）
- 避坑清单（C# 做 AI 的 4 个短板 + 对策）
- 7 条关键设计原则
- 安全与鉴权（JWT / RBAC / 多租户 / Prompt 注入 / 沙箱逃逸 / 审计日志）
- Vibe Coding 使用说明
- 附录 A：核心聚合字段与状态枚举
- 附录 B：状态机引擎迁移方案（自研 → CoreWF）
- 附录 C：多 Agent 协作机制详解（C.1~C.7）
- 附录 D：多模型统一调用机制详解
- 附录 E：vLLM 定位与推理引擎选型
- 附录 F：能力扩展体系（Tool / Skill / MCP 三层架构）
- 附录 G：前端形态选型（Web / 桌面 App / 双形态）
