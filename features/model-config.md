# F13 · 多租户凭据配置（模型 + 搜索外部服务，BYO-Key + 平台内置）  [P0 最高优先级]

> 设计文档（features/ 设计枢纽）。本 feature 为 🔴高风险（破坏性后端改造 + 密钥安全 + 多租户隔离）：改 `IModelClient` / `ISearchProvider` 解析链路为 per-tenant、新增密钥落库加密、新增端点/前端设置。**§7 决策已于 2026-07-27 全部锁定（S1–S6）**，可进实现。

## §1 目标
补齐平台多租户化的最后一环——**外部 API 凭据层租户隔离**。当前数据层已租户隔离，但 LLM 调用层与搜索调用层都是全局单租户（所有租户共用运营方密钥、同一账单）。本 feature 交付双轨（同时覆盖**模型**与**搜索**两类凭据）：

- **A · 用户自配凭据（BYO-Key）**：租户在自己的设置里填 provider / API Key / BaseUrl / 模型名（模型）或 SerpApi Key（搜索），隔离成本、隐私、合规、provider 选择。
- **B · 平台内置凭据**：运营方在 `appsettings` 配好的密钥，作为可选项暴露给所有租户（模型 = `platform-*` 模型；搜索 = 平台默认 SerpApi key），替代现在的哑 `StubModelClient` 占位 / 全局 `SearchSettings` 硬编码，让用户首次打开即有真服务可用（= 试用/上手层）。

两类凭据共用同一套租户感知的解析链路 + 加密落库 + RBAC；B 是 A 的「无密钥降级 + 上手」形态。

## §2 现状核验（已读真实代码，非臆测）
- **模型客户端是全局、server-side-only、启动时一次性构建**：`SemanticKernelModelClient`（`Infrastructure/Models/SemanticKernelModelClient.cs:30`）在构造函数读 `IConfiguration` 的 `OpenAI:Key`/`DeepSeek:Key`/`VLLM:Url`，把 `IChatCompletionService` 注册进 `_services` 字典，**之后不可变**。仅支持 OpenAI 兼容端点（`AddOpenAIChatCompletion`，含自定义 `endpoint` URI → 故 DeepSeek/vLLM/Custom 均走 OpenAI 兼容协议）。
- **注册为 Scoped 但构建只用全局配置**：`DependencyInjection.cs` 中 `AddScoped<SemanticKernelModelClient>` + 装饰 `ModelTelemetryDecorator`；无租户解析 → 所有租户共用同一组密钥。
- **`ModelRouter` 候选来自全局 `RouterSettings.Candidates`**（`Application/Routing/Services/ModelRouter.cs:99`），非租户级。
- **搜索客户端同样全局、构造时固化 key**：`SerpApiSearchProvider`（`Infrastructure/Search/SerpApiSearchProvider.cs:14`）在构造函数经 `IOptions<SearchSettings>` 把 `SerpApiKey` 固化进私有 `_settings`（`SearchSettings.SerpApiKey`），每次 `SearchAsync` 直接读它（`SerpApiSearchProvider.cs:32,44-45`）。改 key 需改环境变量 `Search__SerpApiKey` + 重启；**所有租户共用运营方 SerpApi key、不租户隔离、不可前台配**。`ResearchCommandHandler`（`Application/Research/ResearchCommandHandler.cs:31`）注入 `ISearchProvider` + `IOptions<SearchSettings>`，搜索走全局配置。
- **`TenantProvider` 是 per-request**（`Infrastructure/Persistence/TenantProvider.cs:36`）：从 JWT `tenant_id` 声明 / `X-Tenant-Id` 头 / 默认租户解析。**这是数据层多租户的来源，模型层与搜索层却没用它**。
- **`TenantSettings` 仅有 `DefaultTenantId`**（`Application/Abstractions/TenantSettings.cs`）——无凭据设置。
- **密钥加密基件已就绪**：`IApiKeyEncryptionService`（`Application/Abstractions/IApiKeyEncryptionService.cs`）→ `EncryptKey(plaintext) → (EncryptedKey, KeyPrefix)`、`DecryptKey(encryptedKey) → plaintext`，底层 AES-256-GCM（`ApiKeyEncryptionService`/`AesGcmEncryptor`）。`KeyPrefix` 是前 8 字符——正好用于前端掩码展示（`••••` + prefix）。**直接复用，不自造**。
- **配额基件已就绪但为全局**：`ICostController`（`Application/Abstractions/ICostController.cs`）→ `TryReserve(ModelCandidate,int)`/`SettleUsage(...)`/`ReleaseReservation(...)`，现 `CostController`（`Routing/Services/CostController.cs:43`）按**全局** `_todaySpent` 控日预算，**不区分租户**。B 的防滥用需扩展为租户键控（见 §3.6）。
- **抽象稳定**：`IModelClient`（`ChatAsync`/`ChatStreamAsync`/`GetHealthAsync`）、`ModelResponse(Content,TokenUsage?,ModelId,FinishReason)` 不变；`ISearchProvider`（`SearchAsync(query,maxResults,ct) → SearchResult`）、`SearchResult(Success,ErrorMessage,Snippets)` 不变 → `ResearchCommandHandler` 无需改。
- **前端**：React 19 + Vite + TS(strict) + Antd 5；`api.ts` 单 axios `baseURL:'/api/v1'` + `withCredentials:true`；菜单经 `menuItems` 数组加项；**无设置页**。RBAC：Phase 5 每 key 有角色，`[Authorize]` 已用于多数端点。
- **EF 铁律**（memory）：新增聚合 = **必须 `dotnet ef migrations add`** 生成迁移（`DatabaseInitializer.MigrateAsync` 为 schema 唯一来源）；新迁移文件需 `#pragma warning disable IDE0161`（EF 工具生成 block-scoped namespace，项目强制 file-scoped 为 build error）；`dotnet-ef` 在 `~/.dotnet/tools`。

## §3 拟改接口契约（后端）

### 3.1 聚合 `TenantCredentialSetting`（Domain，新增 · 通用凭据）
- 字段：`Guid Id`（**`ValueGeneratedNever()`**，规避 EF「Guid 主键 `ValueGeneratedOnAdd` + 预置 `Guid.NewGuid()` → UPDATE 命中 0 行」陷阱，见 MEMORY.md）、`Guid TenantId`（实现 `ITenantScoped` → `HasQueryFilter` 自动租户隔离）、`CredentialCategory`(枚举 **Model / Search**)、`Provider`(string，模型=OpenAI/DeepSeek/VLLM/Custom；搜索=SerpApi，v1 仅单一 provider)、`string EncryptedApiKey`（密文）、`string ApiKeyPrefix`（前 8 字符，掩码展示用）、`string? BaseUrl`、`string? ModelName`（仅模型类用）、`bool IsEnabled`、`DateTime CreatedAt`/`UpdatedAt`。
- 行为：构造/`Update(plaintextKey, ...)` 调 `IApiKeyEncryptionService.EncryptKey` 得 `(EncryptedKey, KeyPrefix)`；`GetDecryptedKey()` 调 `DecryptKey`（仅服务端内部用，绝不外泄）。
- 仓储：`ITenantCredentialSettingRepository`（`GetAllByTenantAndCategoryAsync` / `GetByIdAsync` / `AddAsync` / `UpdateAsync` / `DeleteAsync`）；EF 配置 `TenantCredentialSettingConfiguration`：`ToTable("TenantCredentialSettings")`、增 `Name` 列、`(TenantId, Category)` 改为非唯一索引、`HasQueryFilter(t => t.TenantId == tenantId)`、`Id` 显式 `ValueGeneratedNever`。
- **新增 EF 迁移**：`dotnet ef migrations add AddTenantCredentialSetting`（含 `#pragma warning disable IDE0161`）。

### 3.2 凭据解析器（核心改造，最小契约变更）
- 新增 `ITenantCredentialResolver`（`Application.Abstractions`）：
  ```csharp
  Task<TenantCredentialSetting?> ResolveAsync(Guid tenantId, CredentialCategory category, CancellationToken ct = default);
  ```
- 实现 `TenantCredentialResolver`（`Infrastructure`）：
  1. `repo.GetByTenantAndCategoryAsync(tenantId, category)`；
  2. 命中 → 返回该设置（调用方自行解密 key 用）；
  3. 未命中 → 返回 `null`（由各自链路回退平台默认）。
- **缓存失效**：以 `tenantId + category + 设置版本/哈希` 为 key 的 `IMemoryCache` 缓存解析结果；`PUT` 改设置时使该 key 失效，避免每请求重建 kernel / 重读 key。

### 3.3 模型客户端租户化（复用 resolver）
- 新增 `ITenantModelClientResolver`（`Application.Abstractions`）：`Task<IModelClient?> ResolveAsync(Guid tenantId, ct)` → 内部调 `ITenantCredentialResolver`(category=Model)。
- 实现 `TenantModelClientResolver`（`Infrastructure`）：
  1. `cred = await _resolver.ResolveAsync(tenantId, Model)`；
  2. 若 `cred != null && IsEnabled` → 用其解密 key + provider + baseUrl + model 注册 `openai:{model}` / `{provider}:{model}`（复用 `SemanticKernelModelClient` 抽出的共享 factory 方法，不复制）；
  3. 缓存 `IModelClient`（按 §3.2 失效策略）；返回 `new ModelTelemetryDecorator(new SemanticKernelModelClient(services), logger)`；若无 → 返回 `null`（由 §3.4 回退平台模型）。
- `SemanticKernelModelClient` 改造：抽 `BuildServicesFromConfig(key, apiUrl, modelName, prefix)` 共享 factory 方法（原构造函数逻辑外用），使其既能被全局 `IConfiguration` 路径调用，也能被 resolver 按租户参数调用。构造函数保持兼容（启动无租户上下文时仍按全局配置注册，作为平台默认）。

### 3.4 `ModelRouter` 候选合并（平台 ∪ 租户）
- `ModelRouter.RouteAsync` 改为注入 `ITenantModelClientResolver` + `ITenantProvider`：
  - `tenantId = _tenantProvider.GetTenantId()`；
  - `tenantClient = await _modelResolver.ResolveAsync(tenantId, ct)`；
  - 候选 = `tenantClient != null ? tenantClient 注册模型 : 平台模型`；平台模型来自新增 `IPlatformModelProvider`（读 `RouterSettings.Candidates` + 运营方 `appsettings` 已配密钥）；
  - `ChatAsync` 改走对应 `IModelClient`（租户或平台）。
- `IModelClient` 接口不变；**降级链**：租户有配置 → 用租户 key；否则平台有配置 → 用平台 key；否则 `StubModelClient`（占位，明确告知去填 key 或选平台模型）。

### 3.5 搜索客户端租户化（复用同一 resolver，本次重点）
- `SerpApiSearchProvider` 改造（**核心改造点**）：**不再在构造时固化 `SearchSettings.SerpApiKey`**。改为注入 `ITenantCredentialResolver` + `ITenantProvider` + `IOptions<SearchSettings>`（仅取 `BaseUrl`/`TimeoutSeconds`/`DefaultMaxResults` 等**非密钥**参数）。
  - `SearchAsync` 运行时：`tenantId = _tenantProvider.GetTenantId()`；
    - `cred = await _resolver.ResolveAsync(tenantId, Search)`；
    - 若 `cred != null && IsEnabled` → 用 `cred.GetDecryptedKey()` 作 `api_key` 参数（`SerpApiSearchProvider.cs:44-45`）；
    - 否则回退平台默认 `SearchSettings.SerpApiKey`（B 平台内置搜索）；
    - 两者皆空 → 返回 `SearchResult(false, ..., "搜索 API 密钥未配置（请在设置中填写 SerpApi Key 或联系运营方）")`。
  - `ResearchCommandHandler` **零改动**（仍注入 `ISearchProvider`）；其余搜索逻辑（解析 `organic_results`、超时、错误透传）不变。
- 平台内置搜索（B）：运营方 `SearchSettings.SerpApiKey`（`appsettings`/`Search__SerpApiKey`）作为默认，无 BYO 搜索的租户 Research 也能真实联网检索。

### 3.6 平台内置凭据（B）+ 配额
- **模型**：新增 `IPlatformModelProvider`（`Application.Abstractions`）暴露运营方已配密钥对应的模型候选（`RouterSettings.Candidates` 经 `SemanticKernelModelClient` 注册成功的模型），供所有租户在无 BYO-Key 时选。新增 `PlatformModelsController.GET /api/v1/models`（`[Authorize]`，全认证用户；仅返回 `ModelId`/`Provider`/`DisplayName`，不含密钥）。
- **搜索**：平台默认 SerpApi key 即内置（无需额外端点，走 §3.5 回退）。
- **配额（B 防滥用，扩展现有基件）**：`ICostController` 扩展为**租户键控**：`TryReserve(..., Guid tenantId)` / `SettleUsage(..., Guid tenantId)` / `ReleaseReservation(..., Guid tenantId)`；内部 `_todaySpent` 改为 `Dictionary<Guid,Money>` + 全局上限并存。`RouterSettings` 增 `PerTenantDailyBudget`（模型）；**搜索另增 `PerTenantDailySearchQuota`（次数）**——Search API 按次计费，平台内置搜索超配额即提示配 BYO-Key。BYO-Key（A）默认不限（成本归租户自己）。

### 3.7 端点与 DTO（Api）
- 通用 `TenantCredentialsController`：`[Route("api/v1/tenant/credentials")]`、`[ApiController]`、**`[Authorize(Roles="Admin,Operator")]`**（密钥属高敏）。
  - `GET ?category=Model|Search` → `TenantCredentialDto`（`Category`、`Provider`、`ApiKeyMask`(=`••••`+prefix)、`BaseUrl`、`ModelName?`、`IsEnabled`，**绝不返回明文 key**）。
  - `PUT` → `UpdateTenantCredentialRequest`（`CredentialCategory`[Required]、`Provider`[Required]、`string? ApiKey`(明文，仅入站，服务端加密后丢弃)、`string? BaseUrl`、`string? ModelName`、`bool IsEnabled`）；成功后使该 `tenantId+category` 缓存失效。
  - 缺省返回 204/空（租户尚未配置 → 前端提示去填或选平台内置）。
- `TenantCredentialDto` / `UpdateTenantCredentialRequest`（`Api.Models`）。
- 前端（S4 锁定：并入 Agent 配置页，不新增独立页面）：在现有 **Agent 配置页**内嵌 `Tabs: 模型 + 搜索` 两个凭据配置区，结构同构：`Form` + `Input.Password` 掩码 + provider `Select`（模型=OpenAI/DeepSeek/VLLM/Custom；搜索=SerpApi）+ BaseUrl/ModelName 输入 + 保存；Agent/会话创建处模型下拉接 `GET /api/v1/models`（含平台模型 + 若租户自配则并列）；侧栏 `menuItems` **不**新增项。**（S4 最后一项已完成：Agent 创建页「+ 新建 Agent」Modal 与会话详情页顶栏「选择模型」下拉均已接 `GET /api/v1/models`——平台模型 / 我的模型 分组并列；会话选中模型经 `sendMessage(model=modelId)` 透传为 `PreferredModel` 路由。）**

## §4 数据模型
- **新增聚合 `TenantCredentialSetting` + 表 `TenantCredentialSetting` + EF 迁移**（必须 `dotnet ef migrations add AddTenantCredentialSetting`）。
- 密钥加密复用 `IApiKeyEncryptionService`（AES-256-GCM），落库仅存密文 `EncryptedApiKey` + `ApiKeyPrefix`。
- 不改动 `Message`/`Workflow`/`KnowledgeDocument` 等既有表。
- `ITenantScoped` → `HasQueryFilter` 自动隔离，与现有租户数据一致。

## §5 验收标准
- **租户隔离（A 类 · 模型）**：租户 A 配 `deepseek:xxx`；断言租户 A 的 `SendMessage` 实际请求带 A 的 key/endpoint（用 `StubHttpMessageHandler` 镜像 `NativeToolExecutorTests` 验证请求构造）；租户 B 看不到 A 的 key、`GET /api/v1/tenant/credentials?category=Model` 仅返回 B 自己数据（跨租户返回空/403）。
- **租户隔离（A 类 · 搜索 · 本次重点）**：租户 A 配 SerpApi key `aaa`、租户 B 配 `bbb`；断言 `SerpApiSearchProvider.SearchAsync` 在租户 A 上下文发出的请求 `api_key=aaa`、租户 B 上下文 `api_key=bbb`（用 `StubHttpMessageHandler` 捕获请求 URL 断言）；租户 B 取不到 A 的 key。Research 跑通「plan → 用租户 key 真实 SerpApi 检索 → synthesize」。
- **密钥安全**：DB 列 `EncryptedApiKey` 为密文（非明文）；`GET` 返回 `ApiKeyMask`（`••••`+prefix），**任何响应/日志不含明文 key**；`PUT` 入站明文 key 处理后即丢弃（模型与搜索同标）。
- **平台内置凭据（B 类）**：运营方 `appsettings` 配 DeepSeek key 时 `GET /api/v1/models` 返回该平台模型；无 BYO-Key 的租户 `SendMessage` 走平台模型返回真实回复。运营方配 `Search__SerpApiKey` 时，无 BYO-SerpApi 的租户 Research 仍能真实联网检索（平台内置搜索）。
- **降级链**：租户无配置且无平台 key → 模型走 `StubModelClient`（占位提示）；搜索返回 `Success=false` 明确提示配 key。配了即生效（缓存失效正确）。
- **配额**：平台模型超 `PerTenantDailyBudget` / 平台搜索超 `PerTenantDailySearchQuota` → 拒绝并提示配 BYO-Key；BYO-Key 不受限。
- **QA**：`dotnet build` 0 error/warning；`dotnet test` 全绿（含新增租户隔离/加密/路由合并/搜索租户化单测）；前端 `tsc --noEmit` 0 error + `node scripts/qa.mjs` 全绿。
- **EF 不回归**：既有 238 测试仍全绿（尤其 `Message`/`Workflow`/`KnowledgeDocument` 的 `ValueGeneratedNever` 落库路径）。

## §6 质量门清单（嵌入本设计文档，Phase 5 消费）
- **P0（阻断）**：
  - 密钥**落库加密**（AES-256-GCM，复用 `IApiKeyEncryptionService`），明文 key 不入 DB、不出 API、不进日志（模型 + 搜索同标）。
  - 租户凭据隔离：A 的 key/请求绝不泄漏到 B；`HasQueryFilter` 生效（模型 + 搜索同标）。
  - 凭据解析链路 per-tenant，且配置变更即时失效缓存（无 stale key）。
  - `TenantCredentialSetting.Id` **`ValueGeneratedNever()`**（规避 EF 并发陷阱）。
  - `SerpApiSearchProvider` **运行时按租户解析 key**（消除构造时固化全局 key 的隐患）。
- **P1（高）**：
  - `IModelClient` / `ISearchProvider` 接口零改动（`ResearchCommandHandler` 零改动）；仅 `ModelRouter` / `SerpApiSearchProvider` 改走 resolver + 平台回退。
  - 新增端点 RBAC `Admin,Operator`；平台模型端点 `Authorize` 全认证用户。
  - 配置经 `IOptions<T>` / 仓储，不写死密钥/URL。
  - 新增聚合**必须**有 EF 迁移（`MigrateAsync` 为 schema 来源）。
- **P2（中）**：
  - 结构化日志：租户模型/搜索调用记录 tenantId（脱敏）、provider、model、耗时、token/次数；**禁记明文 key**。
  - 单测覆盖：租户隔离（模型 + 搜索）、平台凭据（B）、降级链、密钥加解密往返、路由/搜索解析合并、配额超限。
- **P3（低）**：
  - 前端 `CredentialSettingsPage` 类型一一对应（`types/index.ts`），无 `any`；掩码输入体验正确。
  - 死代码/空 catch 清理；`appsettings.json` 加 `Router:PerTenantDailyBudget` / `Search:PerTenantDailySearchQuota` 注释。

## §7 风险与范围决策（✅ 2026-07-27 已锁定 S1–S6）
本 feature 为高风险（破坏性后端 + 密钥安全 + 多租户）。以下决策点**已由用户拍板锁定**，实现须严格遵循：
- **S1 模型 provider 范围（v1）= OpenAI 兼容（锁定）**：仅 `OpenAI` / `DeepSeek` / `VLLM` / `Custom`（均走 OpenAI 兼容协议，复用现有 `SemanticKernelModelClient.AddOpenAIChatCompletion` + 自定义 `endpoint`）。Anthropic/Google 原生协议不在 v1。
- **S2 平台内置凭据（B）= 启用 + 默认每租户日配额（锁定）**：B 默认开启（运营方 `appsettings` 已配密钥即暴露为 `platform-*` 模型 / 平台默认 SerpApi key），成本由运营方承担，作为上手/试用层。防滥用默认配额（经 `IOptions<RouterSettings>` / `SearchSettings`，可改）：
  - 模型：`PerTenantDailyBudget = 1.00`（USD/租户/天，平台模型累计花费超限即拒并提示配 BYO-Key）；
  - 搜索：`PerTenantDailySearchQuota = 100`（次/租户/天，平台内置搜索超次即拒并提示配 BYO-Key）。
  - BYO-Key（A）不设限（成本归租户自己）。关闭 B 仅需配置开关 `Platform:BuiltInModelsEnabled=false`（预留，v1 默认 true）。
- **S3 每租户每类凭据数（v1）= 多条（列表，由用户 2026-07-27 决策反转原"单条"锁定）**：一个租户可配置**多个不同模型**（不同 Provider / 密钥 / 模型名），搜索类同理。后端由"单条 upsert"改为**列表 CRUD**：`GET /api/v1/tenant/credentials?category=` 返回该租户该类全部凭据（数组，可空）；`POST` 新增；`PUT /api/v1/tenant/credentials/{id}` 按 Id 更新；`DELETE /api/v1/tenant/credentials/{id}` 按 Id 删除。`TenantCredentialSetting` 增 `Name`（列表内显示名）列，`(TenantId, Category)` 唯一索引改为非唯一；`ITenantCredentialResolver` 返回 `IReadOnlyList`；`ModelRouter` 按 candidate→client 映射支持多 BYO key 并存路由。前端「我的凭据」页以表格展示全部模型/搜索凭据，支持新增/编辑/删除。
- **S4 前端范围 = 并入 Agent 配置页（锁定，由实现者设定）**：**不新增独立菜单项/页面**；在现有 **Agent 配置页**内嵌 `Tabs: 模型 + 搜索` 两个凭据配置区（结构与后端同构：provider Select + ApiKey `Input.Password` 掩码 + BaseUrl/ModelName 输入 + 保存）；Agent/会话创建处的模型下拉接 `GET /api/v1/models`（含平台模型 + 若租户自配则并列）。
- **S5 搜索凭据纳入本 feature = 是（锁定）**：`TenantCredentialSetting` 增 `Category=Search`，复用加密/隔离/RBAC/掩码全套基件；`SerpApiSearchProvider` 改为运行时按租户解析 key。
- **S6 搜索 provider 范围（v1）= 仅 SerpApi（锁定）**：对齐 F6 S1 决策 `Provider="SerpApi"`；多搜索方（Brave/Tavily）留待后续（DI 已按 `Provider` 选择，易扩展）。
- 其余默认：`CredentialCategory` 枚举（Model/Search）、掩码 = `••••`+prefix、`StubModelClient` 作模型最终兜底。

## §8 质量门记录（实现后填）
- 8.1 **ddd-code-reviewer**：PASS（对抗式审查覆盖全部 F13 后端文件；修复 1 个 P0：TenantCredentialsController.Put 直接写仓储但未提交 IUnitOfWork.SaveChangesAsync（本控制器不走 MediatR 命令、无 UnitOfWorkBehavior 自动提交）→ 凭据永不落库；已注入 IUnitOfWork 显式 SaveChangesAsync 与命令处理器行为一致；新增 EF 集成测试 TenantCredentialSettingRepositoryTests 锁定落库+租户隔离+upsert 不重复行。核对真实副作用：① 密钥加密——TenantCredentialSetting 仅存 EncryptedApiKey+ApiKeyPrefix，PUT 入站明文加密即丢弃，GET 返回 apiKeyMask=••••+prefix（绝不明文）；② 租户隔离——TenantCredentialSetting:ITenantScoped → AppDbContext.HasQueryFilter 自动隔离，TenantCredentialResolver 按 tenantId 解析，集成测试断言租户 B 取不到 A 的凭据；③ 运行时按租户解析——SerpApiSearchProvider 每次 SearchAsync 经 TryResolveTenantKeyAsync(tenantId) 取 key，BYO key 绕过平台配额（SerpApiSearchProviderTests 断言 api_key=aaa 非平台 key）；④ ValueGeneratedNever——TenantCredentialSettingConfiguration.Id 显式 ValueGeneratedNever；⑤ 接口零改动——IModelClient/ISearchProvider/ResearchCommandHandler 未改；⑥ RBAC——TenantCredentialsController[Authorize(Roles=Admin,Operator)]、PlatformModelsController[Authorize]。P0/P1/P2/P3=0（审查后））
- 8.2 **ddd-phase-quality-gate**：PASS（P0=0 P1=0 P2=1 P3=0；12 类审计：DI 注册完整(TenantCredentialSettingRepository/TenantCredentialResolver/TenantModelClientResolver/IPlatformModelProvider 均 Scoped 注册 + AddMemoryCache)/DDD 分层(接口 Abstractions·实现 Infrastructure·DI Infrastructure)/有 EF 迁移需求已生成 AddTenantCredentialSetting(ValueGeneratedNever)/无硬编码密钥(密文落库+配置走 IOptions)/CancellationToken 全链路透传/实现类 internal sealed/IMemoryCache 缓存仅密文实体非 Singleton 自管集合/空 provider 守卫(TenantCredentialSetting 构造校验)/[ApiController] 自动校验/IOptions<T> 注入/新增枚举常量无零引用/XML 中文注释齐备/Swagger 沿用全局；P2=1 为单测覆盖项——已补 EF 集成测试后实质达标；P3=0；checklist 已嵌 features/model-config.md §6）
- 8.3 **codebase-optimizer**：PASSED（七维度扫描 F13：架构——ModelRouter/TenantModelClientResolver/SerpApiSearchProvider 各自独立承担解析职责、平台↔租户降级链清晰；代码质量——0 any（前端 tsc 0 error）+ strict + internal sealed + 中文 XML + 前端 Input.Password 掩码；正确性——真实 HttpClient（SerpApiSearchProviderTests 用 StubHttpMessageHandler 验证真实 GET 构造）、真实加密往返（IApiKeyEncryptionService）、EF 集成测试验证真实落库；测试——新增 11 例（TenantModelClientResolverTests 3 + SerpApiSearchProviderTests BYO 2 + TenantCredentialSettingRepositoryTests 1 + 既有 CostController/AgentRouting 改造）；性能——HttpClient 走 IHttpClientFactory 池化 + 请求级超时 CancellationTokenSource、凭据解析按 tenantId 缓存 + PUT 失效；安全——密钥 AES-256-GCM 落库、明文不出 API/不进日志、BYO 绕过平台配额防滥用；工程化——dotnet build 0 警告 0 错误 + tsc --noEmit 0 错误 + 全方案测试全绿，前端无死代码；按 feature-builder 约束在 feat/f13-multi-tenant-credentials 分支分析+修复，未新建分支或推送）
