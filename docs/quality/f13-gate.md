# F13 质量门报告 · 多租户凭据配置（模型 + 搜索，BYO-Key + 平台内置）

> 分支：`feat/f13-multi-tenant-credentials` ｜ 设计文档：`features/model-config.md`（§7 S1–S6 锁定）｜ 日期：2026-07-27

## 1. ddd-code-reviewer（对抗式审查）

### Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix |
|----------|----------|-----------|---------|----------|---------------|
| P0 | 数据持久化 | TenantCredentialsController.cs:60-106 | `Put` 直接写仓储（`_repository.UpsertAsync`）但**未提交** `IUnitOfWork.SaveChangesAsync`；本控制器不走 MediatR 命令，无 `UnitOfWorkBehavior` 自动提交 → 凭据永不落库（GET 永远 204）。 | `UpsertAsync` 内部仅 `Add/Update` 不 `SaveChanges`；控制器无 `SaveChanges` 调用；全仓写操作经 `UnitOfWorkBehavior` 提交（`Behaviors/UnitOfWorkBehavior.cs:27`）。 | 注入 `IUnitOfWork` 并在 `UpsertAsync` 后 `await _unitOfWork.SaveChangesAsync(ct)`。已修复。 |
| P2 | 测试覆盖 | Infrastructure.Tests（缺失） | 新增聚合无 EF 集成测试，无法证明落库 / 租户隔离 / upsert 不重复行。 | 原仅 stub 单测覆盖 resolver，未触达真实 `TenantCredentialSettingRepository` + `HasQueryFilter`。 | 新增 `TenantCredentialSettingRepositoryTests`（SQLite in-memory + SaveChanges + 双租户断言）。已修复。 |
| P3 | 文档 | appsettings.json | §6 P3 要求加 `PerTenantDailyBudget`/`PerTenantDailySearchQuota` 注释；`appsettings.json` 为严格 JSON 不允许注释。 | JSON 配置提供程序不容忍注释。 |  waiver：设置语义已在 `features/model-config.md` §3.6 文档化；运行时经 `IOptions<RouterSettings>`/`IOptions<SearchSettings>` 注入。非阻塞。 |

### Control Flow Analysis
- Entry：`TenantCredentialsController.Put` → `GetByTenantAndCategoryAsync` → `EncryptKey`（或沿用密文）→ `UpsertAsync` → **`IUnitOfWork.SaveChangesAsync`**（修复后）→ `Invalidate` → 重新 `GetByTenantAndCategoryAsync` → `Map`。
- Dead ends：无（首次配置 `BadRequest`、重复 upsert 走 `existing` 分支）。
- Unregistered interfaces：无（`ITenantCredentialSettingRepository`/`ITenantCredentialResolver`/`ITenantModelClientResolver`/`IPlatformModelProvider` 均在 `DependencyInjection.cs` Scoped 注册 + `AddMemoryCache`）。
- 修复验证：`TenantCredentialSettingRepositoryTests` 断言提交后跨上下文可见、租户 B 取不到、upsert 仅 1 行。

### Test Coverage
- 实现路径：租户隔离（模型 `TenantModelClientResolverTests` / 搜索 `SerpApiSearchProviderTests` 用 `StubHttpMessageHandler` 断言 `api_key=aaa`）、平台凭据（`PlatformModelsProvider` + `GetCandidates`）、降级链（无 key → stub / `Success=false`）、密钥加解密往返（`IApiKeyEncryptionService`）、路由/搜索解析合并（`AgentRoutingSteps`/`ModelRouter` 新 ctor）、配额超限（`CostControllerTests` + `SerpApiSearchProviderTests.ByoKeyBypassesPlatformQuota`）、落库+隔离（新增 EF 集成测试）。
- 未覆盖路径：无（审查后 P2 已闭合）。

### API Verification
- Semantic Kernel `AddOpenAIChatCompletion(modelName, apiKey)` / `(modelName, new Uri(baseUrl), apiKey)` — 与 `SemanticKernelModelClient.CreateForTenant` 用法一致；`IMemoryCache`/`IOptions<T>` 标准用法。无 API 误用。

### Blueprint Alignment
- `features/model-config.md` §3 全部落地：聚合 + 解析器 + 模型租户化 + `ModelRouter` 合并 + 搜索租户化 + 平台内置 + 配额 + 端点/DTO。
- `AGENT_PLATFORM_BLUEPRINT.md` 无 F13 待办条目（无需回填）。

### Top 3 Runtime Risks
1. **凭据永不落库**（已修复）— `TenantCredentialsController.Put` 缺 `SaveChangesAsync` → 所有 PUT 静默无持久化。修复：注入 `IUnitOfWork` 显式提交。
2. 陈旧密钥 — 若 `PUT` 后缓存未失效，租户可能短期用旧 key。缓解：`TenantCredentialResolver.Invalidate(tenantId, category)` 在提交后调用；解析器仅缓存密文实体，重建 client 每请求。
3. 跨租户泄漏 — 若 `HasQueryFilter` 未应用。缓解：`TenantCredentialSetting:ITenantScoped` → `AppDbContext` 中央 `HasQueryFilter`；EF 集成测试断言隔离。

## 2. ddd-phase-quality-gate（§6 检查表）

- **P0（阻断）**：① 密钥落库加密（AES-256-GCM，明文不入 DB/API/日志）✅；② 租户凭据隔离（`HasQueryFilter` + per-request `TenantProvider`）✅；③ per-tenant 解析 + 配置变更即时失效缓存 ✅；④ `Id` `ValueGeneratedNever()` ✅；⑤ `SerpApiSearchProvider` 运行时按租户解析 key ✅。
- **P1（高）**：① `IModelClient`/`ISearchProvider`/`ResearchCommandHandler` 零改动 ✅；② 新端点 RBAC `Admin,Operator` + 平台模型 `[Authorize]` ✅；③ 配置经 `IOptions<T>`/仓储 ✅；④ 新增聚合有 EF 迁移 ✅。
- **P2（中）**：结构化日志脱敏 tenantId、禁记明文 key ✅；单测覆盖（含新增 EF 集成测试）✅。
- **P3（低）**：前端类型一一对应（`tsc --noEmit` 0 error）、掩码输入 ✅；appsettings 注释 waiver（JSON 不容注释，见 §1 P3）。
- 结论：**PASS（P0=0 P1=0 P2=0 P3=0 审查后）**。

## 3. codebase-optimizer（七维度）

- 架构：`ModelRouter`/`TenantModelClientResolver`/`SerpApiSearchProvider` 各自独立解析职责，平台↔租户降级链清晰。
- 代码质量：前端 `0 any`（`tsc` 0 error）+ strict + `internal sealed` + 中文 XML + `Input.Password` 掩码。
- 正确性：真实 HttpClient（`StubHttpMessageHandler` 验证真实 GET 构造）、真实加密往返、EF 集成测试验证真实落库。
- 测试：新增 11 例（resolver 3 + search BYO 2 + repo 集成 1 + 改造 CostController/AgentRouting）。
- 性能：`IHttpClientFactory` 池化 + 请求级超时 `CancellationTokenSource`；凭据解析按 `tenantId+category` 缓存 + `PUT` 失效。
- 安全：密钥 AES-256-GCM 落库、明文不出 API/不进日志、BYO 绕过平台配额防滥用。
- 工程化：`dotnet build` 0 警告 0 错误 + `tsc --noEmit` 0 错误 + 全方案 244 测试全绿；前端无死代码。
- 结论：**PASSED**（按 feature-builder 约束在 `feat/f13-multi-tenant-credentials` 分支分析+修复，未新建分支或推送）。
