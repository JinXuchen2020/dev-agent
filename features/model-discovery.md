# F14 · 供应商模型发现（填 Key + Base URL 后拉取可访问模型清单）  [P0 最高优先级]

> 设计文档（features/ 设计枢纽）。本 feature 为 🔴高风险（新增端点 + 鉴权 + 路由 + 前端契约变更），来源 F13 凭据配置 UX 衍生。**§6 决策已于 2026-07-27 锁定（D1）**，可进实现。
>
> 一句话：用户在「我的凭据」/Agent 配置页填 API Key + Base URL 后，点「拉取模型」即可从该 provider 账户拉回所有可访问模型（`GET /v1/models`，OpenAI 兼容），以下拉供选择，免去手填模型名。

## §1 目标
F13 已交付多租户凭据配置，但 `CredentialForm` 的 **Model Name 仍是纯文本框**——用户必须自己拼对 `gpt-4o` / `deepseek-chat` 等模型 ID，易错且不知道该 provider 到底有哪些模型可用。

本 feature 交付**供应商模型发现**：
- 用户在模型类凭据表单填 `API Key` + `Base URL` + 选 `Provider` 后，点「拉取模型」按钮；
- 后端用该密钥向 provider 的 `GET {baseUrl}/models` 发起请求（OpenAI 兼容协议），解析返回的模型清单；
- 前端把清单填进 **Model Name 下拉（AutoComplete）**，用户直接选；也允许手填自定义模型名（兼容非常规模型 ID）。

范围仅 **模型类凭据**（搜索类无「模型列表」概念，SerpApi 不需要）。Provider 范围对齐 F13 §7 S1/S6：**OpenAI / DeepSeek / VLLM / Custom（均 OpenAI 兼容）**。

## §2 现状核验（已读真实代码，非臆测）
- **模型客户端无「列模型」能力**：`SemanticKernelModelClient`（`Infrastructure/Models/SemanticKernelModelClient.cs`）按 `modelName` 预注册 `IChatCompletionService`，无 `ListModels` 方法。OpenAI 兼容端点都暴露 `GET /v1/models`，但当前代码从未调用。
- **出站 HTTP 模式已有先例**：`SerpApiSearchProvider`（`Infrastructure/Search/SerpApiSearchProvider.cs:21,81-85`）用 `IHttpClientFactory.CreateClient(...)` + 请求级 `CancellationTokenSource` 超时 + 错误透传（非 2xx → 明确原因，不伪造）。本 feature 的 discovery 服务**原样复用该模式**。
- **凭据端点与 RBAC**：`TenantCredentialsController`（`Api/Controllers/TenantCredentialsController.cs:17`）`[Authorize(Roles="Admin,Operator")]`。discovery 端点同门禁（admin 才有 provider key，合理）。
- **密钥加密基件**：`IApiKeyEncryptionService` 已就绪，但本 feature **不需要落库**——discovery 用的 key 仅作一次性探测，绝不写库、绝不写日志。
- **前端表单**：`CredentialForm.tsx:152-159` 的 Model Name 为 `<Input>`（自由文本）；`api.ts` 已有 `createTenantCredential`/`updateTenantCredential`；`types/index.ts` 有 `TenantCredentialDto` / `CreateTenantCredentialRequest`。
- **无 schema 变更**：discovery 不新增聚合/表，故**无需 EF 迁移**（规避 EF 铁律陷阱）。

## §3 拟改接口契约（后端）

### 3.1 新端点 `POST /api/v1/tenant/credentials/discover-models`
- 请求体 `DiscoverModelsRequest`：
  ```csharp
  public sealed record DiscoverModelsRequest(
      string Provider,        // OpenAI / DeepSeek / VLLM / Custom
      string ApiKey,          // 必填；仅用于本次探测，不落库不记日志
      string? BaseUrl = null);// OpenAI 兼容端点；OpenAI/DeepSeek 留空自动补默认
  ```
- 处理流程：
  1. 校验 `Provider` 合法、`ApiKey` 非空（否则 400）。
  2. 解析 base URL（见 §3.2）。
  3. `client = _httpClientFactory.CreateClient(nameof(ProviderModelDiscovery))`；`GET {baseUrl}/models`，头 `Authorization: Bearer {apiKey}`；请求级超时 15s（`CancellationTokenSource`）。
  4. 非 2xx → 400 + 中文原因（`401/403` → "API Key 无效或无权访问"；`404` → "该端点不支持 /models，请检查 Base URL"；其余 → 原状态原因）。
  5. 解析 OpenAI 兼容响应 `{ "object":"list", "data":[ { "id":"gpt-4o", "owned_by":"openai" }, ... ] }`，取每个元素的 `id`（与可选 `owned_by`）。
  6. 返回 `Ok(List<ProviderModelInfo>)`；空清单也返回 200 + 空数组（而非错误）。
- 门禁：`[Authorize(Roles="Admin,Operator")]`（与凭据控制器一致）。
- **安全**：`ApiKey` 仅用于探测；控制器/服务**绝不**写日志含 key；不写库。

### 3.2 Provider → Base URL 解析（默认补全）
- `OpenAI` → `https://api.openai.com/v1`（与 `SemanticKernelModelClient` 默认端点一致，`AddOpenAIChatCompletion(modelName, apiKey)` 的无 endpoint 默认即此）。
- `DeepSeek` → `https://api.deepseek.com/v1`（与 F13 约定一致）。
- `VLLM` / `Custom` → **必须**用户提供 `BaseUrl`，否则 400（"VLLM/Custom 必须填写 Base URL"）。
- 补全后 models URL = `{baseUrl.TrimEnd('/')}/models`（与聊天端点构造对齐：`{baseUrl}/chat/completions` 同基，故 discovery 复用同一 base 契约，避免用户输入歧义）。

### 3.3 新服务 `IProviderModelDiscovery`（Infrastructure）
```csharp
public interface IProviderModelDiscovery
{
    Task<IReadOnlyList<ProviderModelInfo>> DiscoverAsync(
        string provider, string apiKey, string? baseUrl, CancellationToken ct = default);
}
```
- 实现 `ProviderModelDiscovery`：`internal sealed`，注入 `IHttpClientFactory` + `ILogger`；封装 §3.1 的 HTTP + 解析 + 错误映射；超时用 `CancellationTokenSource(TimeSpan.FromSeconds(15))` 与 `ct` 联动（复用 `SerpApiSearchProvider` 的 `CreateLinkedTokenSource` 模式）。
- DTO `ProviderModelInfo`：`record ProviderModelInfo(string Id, string? OwnedBy = null);`
- DI：`services.AddScoped<IProviderModelDiscovery, ProviderModelDiscovery>();`（HTTP 客户端经既有 `IHttpClientFactory`，无需额外 `AddHttpClient` 命名客户端，除非需要统一超时/重试策略——本 feature 超时在方法内自管，保持简单）。

### 3.4 控制器接入
- 在 `TenantCredentialsController` 新增 `[HttpPost("discover-models")]` action；注入 `IProviderModelDiscovery`。
- 注意：本 action 仍走 MVC 控制器、非 MediatR 命令，但**只读探测、无仓储写操作**，故无需 `IUnitOfWork.SaveChangesAsync`（与既有 `GET` 一致，无落库）。

### 3.5 测试（重写/新增）
- `ProviderModelDiscoveryTests`（Infrastructure.Tests）：用 `StubHttpMessageHandler`（或 `FakeHttpMessageHandler`）mock 出站：
  - OpenAI 默认 base + 合法 key → 断言请求 URL = `https://api.openai.com/v1/models`、头含 `Bearer`、返回解析出 `["gpt-4o","gpt-4o-mini"]`。
  - `401` 响应 → 抛/返回明确错误（控制器映射 400）。
  - DeepSeek 默认 base 补全正确；VLLM 缺 baseUrl → 抛参数异常（控制器 400）。
  - 空 `data` → 返回空数组（200）。
- 不引入外部真实网络（CI 安全）；与 `SerpApiSearchProviderTests` 同风格。

## §4 前端契约（React + Antd）

### 4.1 `types/index.ts`
```ts
export interface ProviderModelInfo { id: string; ownedBy?: string | null; }
```

### 4.2 `services/api.ts`
```ts
export const discoverProviderModels = (req: { provider: string; apiKey: string; baseUrl?: string | null }) =>
  api.post<ProviderModelInfo[]>('/tenant/credentials/discover-models', req).then((r) => r.data ?? []);
```

### 4.3 `components/CredentialForm.tsx`（模型类）
- `Model Name` 由 `<Input>` 改为 `<AutoComplete>`：
  - `options` 来自「拉取模型」结果（`{ value: id, label: ownedBy ? `${id}（${ownedBy}）` : id }`）；
  - `AutoComplete` 允许自由输入（自定义模型名），兼顾非常规 ID。
- 新增「拉取模型」按钮（`Space` 内，与保存并列或置于 Model Name 旁）：
  - 可用条件：模型类 + `apiKey` 非空 + （provider ∈ {OpenAI,DeepSeek} 或 `baseUrl` 非空）；
  - 点击 → `discoverProviderModels({provider, apiKey, baseUrl})`；`loading` 态；成功回填 options + `message.success("已拉取 N 个模型")`；失败 `message.error(getErrorMessage(err))`。
- **edit 模式（D1 决策）**：若用户未重填 `API Key`，「拉取模型」按钮禁用并提示「请先填写 API Key 后再拉取」（**不做后端解密存量密钥**——密钥仅在表单内临时使用，不回传 credentialId）。

## §5 验收子项
- **后端**：`discover-models` 端点 OpenAI/DeepSeek 默认 base 补全正确；VLLM/Custom 缺 baseUrl → 400；`401/403/404` → 400 中文原因；合法响应解析 `id` 正确；空 `data` → 200 空数组；`StubHttpMessageHandler` 单测覆盖上述路径。
- **前端**：Model Name 下拉填充 + 允许自定义；「拉取模型」按钮 loading/错误态；edit 模式留空 Key 时按钮提示先填 Key。
- **e2e（Python UTF-8，规避 Git Bash 中文参数 400 假象）**：登录 → 填 Key+BaseUrl → discover → 返回模型列表 → 选一个 → 保存 → `GET /tenant/credentials` 列表含该 model。
- **质量门**：`dotnet build` 0/0、`dotnet test` 全绿（含 discovery 单测）、前端 `tsc --noEmit` 0 error + `vitest` 全过 + `vite build` 通过；实现后追加 `.quality-gate.json` notes 并保 `cleared:true`。

## §6 决策（已锁定）
- **D1 · edit 模式探测密钥来源 = 仅用表单现填 Key**：用户在 edit 表单若未重填 `API Key`，「拉取模型」按钮禁用并要求先填 Key；**不实现**后端按 `credentialId` 解密存量密钥来探测（用户 2026-07-27 拍板：更简单、密钥仅在表单内临时使用）。后续若需更顺滑体验可再放开，但当前按 D1 实现。
- **D2 · 范围 = 仅模型类**：搜索类凭据（SerpApi）无「模型列表」语义，本 feature 不涉及；provider 范围对齐 F13（OpenAI/DeepSeek/VLLM/Custom 均 OpenAI 兼容）。
- **D3 · 无 schema 变更**：discovery 不落库，故无 EF 迁移；密钥仅一次性探测，不写库不写日志。
- **D4 · 安全边界**：端点向用户填写的 provider URL 发起出站请求——Admin 专用、且是用户自有密钥对应的自家 provider，属预期行为；密钥不落库不记日志，避免凭证泄露面。

## §7 风险
- 🔴 高风险：新增端点 + 鉴权 + 路由 + 前端契约变更，触发 feature-dev 高风险闸口（先设计后实现，本设计文档即契约）。
- 出站请求（SSRF 面）：因 Admin 专用 + 用户自有 provider，风险可接受；如需收紧可后续加 provider 域名白名单（不在本 feature 范围）。
- 部分非标准 OpenAI 兼容端点 `/models` 返回结构略有差异：解析时容错（缺 `owned_by` 容忍、非数组 `data` → 视为空/错误提示）。

## Phase Quality Gate Checklist（F14）

> 由 ddd-phase-quality-gate 嵌入（8 类全扫，P0–P3 = 0 open）。增量顺序：接口/实现/端点 → 编译 0 警告 → F14 单测全绿 → DI 审计 → 层审计 → 前端契约对齐 → 提交。

### 1. Pre-flight Version Audit
- [x] `IHttpClientFactory` 复用既有 `AddHttpClient()`（DI 已注册），无新 NuGet 依赖引入。
- [x] `HttpRequestMessage` / `AuthenticationHeaderValue` / `JsonDocument` 均为 .NET 9 BCL API，签名已与代码核对。

### 2. BDD Scenarios First
- [x] 本 feature 为 UX 衍生（来源 F13 §衍生），验收以 `model-discovery.md §5` 子项为准；后端单测覆盖全部探测路径。

### 3. DDD Layer Rules
- [x] 接口 `IProviderModelDiscovery` + `ProviderModelInfo` + `ProviderModelDiscoveryException` 位于 `AgentPlatform.Application.Abstractions`。
- [x] 实现 `ProviderModelDiscovery` 位于 `AgentPlatform.Infrastructure.Models`（`internal sealed`）。
- [x] 端点 `DiscoverModels` 位于 `AgentPlatform.Api.Controllers.TenantCredentialsController`。

### 4. DI Registration Completeness
- [x] `IProviderModelDiscovery` 已在 `Infrastructure/DependencyInjection.cs` 注册为 `AddScoped`（与 `IPlatformModelProvider` 同区块）。
- [x] 控制器 ctor 注入并消费；无未注册接口。

### 5. Configuration-First
- [x] Provider 默认 Base URL 以静态字典常量集中（`DefaultBaseUrls`），非散落魔法字符串；超时 15s 为明确常量。
- [x] 不新增 `IOptions<T>`（本服务无外部配置项；密钥来自请求体、不落库）。

### 6. EF Core Mapping Sync
- [x] 本 feature 无新聚合 / 表 / 迁移（D3 决策：discovery 不落库）。

### 7. Concurrency & Lifecycle
- [x] `ProviderModelDiscovery` 为 Scoped，无 static 可变状态；`HttpClient` 由工厂提供、`HttpRequestMessage`/`HttpResponseMessage`/`JsonDocument` 均 `using` 释放，`CancellationTokenSource` 链接后释放。
- [x] 超时与取消正确联动（请求 + 读取响应体全程受 15s 请求级超时保护，映射为友好 400 而非 500）。

### 8. Cross-Cutting Infrastructure
- [x] 端点 `[Authorize(Roles="Admin,Operator")]`（与凭据控制器一致）；密钥仅一次性探测，不落库、不写日志。
- [x] 错误统一以 `ProviderModelDiscoveryException` 中文原因经控制器 `BadRequest` 返回；非 2xx / 401/403/404 / 超时 / 传输全覆盖。
- [x] 前端 `tsc --noEmit` 0 error + `vite build` 通过；前后端字段 `id`/`ownedBy` 对齐。

