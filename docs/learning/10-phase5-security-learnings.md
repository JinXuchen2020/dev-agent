# 10. Phase 5 学习笔记：把"声称要做"的安全底座真实落地

> 目标：Phase 5 和 Phase 4 一样，不是加功能，而是给"蓝图宣称第一优先级、实际整层缺失"的**安全**补课。本笔记讲清 **七个安全知识点** + **三个真实排障实录**（认证默认方案缺失、Swagger 模拟登录、DB 迁移反模式），以及背后的工程原则。

> **一句话**：Phase 5 把 JWT/API-Key 认证、真实多租户、RBAC、限流、提示注入防护、审计、API Key AES-256-GCM 加密全部真实接线；核心原则是 **默认拒绝、最小攻击面、fail-closed**。
> 配套阶段文档：`phases/phase-5-security-hardening.md`（含验收标准与二次评审闭环记录）。

---

## 10.1 为什么 Phase 5 是"安全加固"而不是"新功能"

蓝图 §9 铁律写"安全是第一优先级，不是以后再补"，但实测落地时**整层安全被遗漏**：认证跳过、`TenantProvider` 硬编码返回 `DefaultTenantId`、RBAC 恒为 Admin、API Key 明文、无审计、无限流。这属于"实现漂移"的 **C 类——蓝图声明是硬约束，实现却当作可选跳过**。

| 能力 | Phase 4 结束时状态 | Phase 5 要做 |
|------|-------------------|-------------|
| 用户认证 | 完全跳过（无 JWT/API-Key） | JWT + API-Key 双方案并存 |
| 多租户隔离 | DB 层 `HasQueryFilter` 建好，但 `TenantProvider` 恒返回默认租户 | per-request 从凭证解析真实 `tenant_id` |
| RBAC | `GetRoles` 恒返回 Admin | per-key 真实角色 |
| API Key 存储 | 无（根本没有 Key） | DB-backed `ApiKey` 聚合 + AES-256-GCM 加密 |
| 审计 | 无 | `AuditLog` 聚合 + 关键操作留痕 |
| 限流 / 注入防护 | 无 | RateLimiter + PromptInjection 中间件 |

**为什么 launch-blocking**：安全是**运营前置门槛**，不是亮点功能。带着"假认证 + 全局同租户 + 明文 Key"上线，等于对外裸奔。所以 Phase 5 独立成阶段，作为任何多用户/对外部署前的硬门槛。

**关键工程认知**：多租户隔离的**数据库层早已建好**（`AppDbContext.OnModelCreating` 对所有 `ITenantScoped` 实体 `HasQueryFilter`），Phase 5 只差把 `TenantProvider` 从硬编码改为按请求解析——这是"小而高杠杆"的改动。**底座建好、开关没接**是这类安全漂移的典型形态。

---

## 10.2 Phase 5 知识地图（七大安全知识点）

```
Phase 5 安全加固 = 给"声称第一优先级、实为整层缺失"的安全补课
┌────────────────────────────────────────────────────────────────────┐
│  ① 认证多方案并存      JWT + API-Key，用 Policy Scheme 按请求头分发   │
│  ② 真实多租户          TenantProvider per-request 从 claim 解析       │
│  ③ RBAC                per-key 真实角色，非恒 Admin                   │
│  ④ API Key 加密+生命周期 AES-256-GCM + DB 聚合 + 轮换/吊销/过期扫描    │
│  ⑤ 提示注入防护        中间件拦截，正则收窄 + 负向测试                 │
│  ⑥ 审计日志            AuditLog 聚合，业务 + Key 操作全留痕            │
│  ⑦ 限流                RateLimiter 按租户/Key 维度限速                │
└────────────────────────────────────────────────────────────────────┘
       ↑ 全部由 ddd-code-reviewer（对抗式，查"名不副实"）
         + ddd-phase-quality-gate（结构卫生）把关
```

---

## 10.3 七大知识点详解

### 知识点 1 · 认证多方案并存（Policy Scheme 分发）

**问题**：JwtBearer + ApiKey 两个方案都注册了，但 `AddAuthentication()` 用空配置——**没有默认方案**。当 `EnforceAuthentication=true` 时，任何 `[Authorize]` 请求发起 challenge 会抛 `No authenticationScheme was specified, and there was no DefaultChallengeScheme found`。

**解决方案**：引入一个 `Smart` **policy scheme** 作为统一默认方案，用 `ForwardDefaultSelector` 按请求头把认证/质询转发给具体方案：
- 带 `Authorization` 头 → 转发到 `Bearer`（JWT）
- 带 `X-API-Key` 头 → 转发到 `ApiKey`
- 无凭证 → 转发到 `Bearer`（返回 `WWW-Authenticate: Bearer`，正确 401）

```csharp
builder.Services.AddAuthentication("Smart")
    .AddPolicyScheme("Smart", "JWT or ApiKey", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.ContainsKey(apiKeyHeaderName) ? "ApiKey" : "Bearer";
    })
    .AddJwtBearer("Bearer", ...)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);
```

**代码落点**：`src/AgentPlatform.Api/Program.cs`（认证块）；`src/AgentPlatform.Infrastructure/Auth/ApiKeyAuthenticationHandler.cs`。

**学到的工程点**：
- **多方案认证必须有"选择器"**：`[Authorize]` 不指定 `AuthenticationSchemes` 时完全依赖默认方案。多方案场景下，默认方案要么写死一个，要么用 policy scheme 动态分发。
- **`NoResult()` vs `Fail()` 的语义**：ApiKey handler 在无 `X-API-Key` 头时返回 `NoResult()`（"我不适用，交给别的方案"），有头但无效才 `Fail()`。这是多方案共存的正确写法——**不要在自己不适用时 Fail**，否则会短路其他方案。

---

### 知识点 2 · 真实多租户（TenantProvider per-request）

**问题**：`TenantProvider` 早期硬编码 `return _settings.DefaultTenantId`——所有请求都落到同一个租户，隔离形同虚设。DB 层的 `HasQueryFilter` 明明建好了，却因为拿不到真实租户 ID 而无效。

**解决方案**：`TenantProvider` 改为 **per-request（Scoped）从 `HttpContext` 的 claim 解析** `tenant_id`：
- 认证通过后，JWT/ApiKey 的 `tenant_id` claim 写入 `HttpContext.User`；
- `TenantProvider.GetTenantId()` 读该 claim，无则回退 `DefaultTenantId`（仅 dev）。

**代码落点**：`src/AgentPlatform.Infrastructure/Persistence/TenantProvider.cs`；契约 `src/AgentPlatform.Application/Abstractions/ITenantProvider.cs`；DB 过滤 `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`（`OnModelCreating` 对 `ITenantScoped` 实体 `HasQueryFilter`）。

**学到的工程点**：
- **隔离要"纵深"**：DB Query Filter（兜底）+ TenantProvider（解析）+ 认证（来源）三层缺一不可。只建 Query Filter 而 Provider 恒返回默认值，等于没隔离。
- **Scoped 生命周期**：TenantProvider 必须是 Scoped（跟随请求），不能 Singleton——否则第一个请求的租户会被后续请求复用。

---

### 知识点 3 · RBAC（per-key 真实角色）

**问题**：`GetRoles` 恒返回 `Admin`——任何认证用户都是超管，角色区分名存实亡。

**解决方案**：角色从**凭证本身**取：JWT 的 `ClaimTypes.Role` claim / ApiKey 记录里存的角色。`[Authorize(Roles = "Admin")]` 等按真实角色判定。三级角色：`Admin > Operator > Viewer`。

**代码落点**：`ApiKeyAuthenticationHandler` 从 `ApiKey` 聚合读角色写入 claim；控制器 `[Authorize(Roles=...)]`。

**学到的工程点**：**认证（你是谁）和授权（你能做什么）要分开**。认证方案负责把身份 + 角色写进 `ClaimsPrincipal`，授权靠 `[Authorize(Roles)]` / policy 判定。角色来源必须是可信凭证，不能在 handler 里写死。

---

### 知识点 4 · API Key 加密 + 全生命周期

**问题**：API Key 若明文入库，DB 泄露即凭证泄露。

**解决方案**：
1. **AES-256-GCM 加密**：`AesGcmEncryptor` 实现 `IAesEncryptor`，`ApiKeyEncryptionService` 消费它对 Key 加解密（GCM 提供认证加密，防篡改）。
2. **DB-backed 聚合**：`ApiKey` 聚合根 + `IApiKeyRepository` + EF 配置，Key 持久化到 `ApiKeys` 表（密文列）。
3. **全生命周期**：`Rotate()`（轮换）/ `Revoke()`（吊销）都是聚合行为；`ApiKeyExpiryJob`（BackgroundService，每 6h 扫描）做过期提醒/清理。

**代码落点**：`src/AgentPlatform.Infrastructure/Security/AesGcmEncryptor.cs`、`ApiKeyEncryptionService.cs`；`src/AgentPlatform.Domain/Aggregates/ApiKeys/ApiKey.cs`；`src/AgentPlatform.Infrastructure/Persistence/Repositories/ApiKeyRepository.cs`；`src/AgentPlatform.Infrastructure/Jobs/ApiKeyExpiryJob.cs`。

**学到的工程点**：
- **用认证加密（AEAD）而非纯加密**：GCM 模式自带完整性校验，比 CBC 更安全，避免密文被篡改后仍能解出垃圾。
- **密钥来源分级**：`AesEncryptionKey` / `JwtSecretKey` 在 dev 是占位值，**生产必须用环境变量/密钥管理覆盖**。硬编码密钥入库 = 没加密。
- **生命周期方法不能是死代码**：`Rotate/Revoke` 必须有真实调用点（ExpiryJob / 端点），否则评审会判"名不副实"。上一轮评审就命中过 `Revoke()` 死代码并修复。

---

### 知识点 5 · 提示注入防护（Prompt Injection）

**问题**：用户输入可能含"忽略以上指令""你现在是 DAN"等注入，劫持 Agent 行为。

**解决方案**：`PromptInjectionMiddleware` + `PromptInjectionService`，用**收窄后的正则**匹配已知注入模式，命中即拦截。配**负向测试**确保正常输入不误杀。

**代码落点**：`src/AgentPlatform.Api/Middleware/PromptInjectionMiddleware.cs`；`src/AgentPlatform.Infrastructure/Security/PromptInjectionService.cs`。

**学到的工程点**：
- **正则要收窄 + 负向测试**：过宽的注入正则会把正常业务文本（如"请忽略排序，按时间"）误判。收窄模式后必须补负向用例（正常输入不拦）。
- 注入防护是**纵深防御的一层**，不是银弹——配合最小权限、输出校验一起用。

---

### 知识点 6 · 审计日志（AuditLog）

**问题**：无法回答"谁在何时对什么资源做了什么"——合规和事故排查都缺证据。

**解决方案**：`AuditLog` 聚合 + `AuditActionType` 枚举 + `IAuditLogRepository`。留痕点覆盖：
- **业务操作**：4 个关键 handler（创建/删除等）；
- **Key 操作**：`KeyUsed` / `KeyRotation` / `KeyRevoked` 三点位。

**代码落点**：`src/AgentPlatform.Domain/Aggregates/AuditLogs/AuditLog.cs`、`Enums/AuditActionType.cs`；`src/AgentPlatform.Infrastructure/Persistence/Repositories/AuditLogRepository.cs`。

**学到的工程点**：审计要记 **who / when / what / result**，且写入本身不能拖垮主流程（失败降级，best-effort）。审计表按租户隔离，避免跨租户可见。

---

### 知识点 7 · 限流（Rate Limiting）

**问题**：无限流 → 单租户/单 Key 可打爆后端和模型配额。

**解决方案**：ASP.NET Core 内置 `RateLimiter`，按租户/Key 维度限速（`RateLimitPerMinute` 配置）。

**代码落点**：`src/AgentPlatform.Api/Program.cs`（`AddRateLimiter` + `UseRateLimiter`）；配置 `appsettings.json` 的 `Security:RateLimitPerMinute`。

**学到的工程点**：限流维度要选对——按 IP 太粗（NAT 后多用户共享），按租户/Key 才是业务隔离粒度。

---

## 10.4 三个真实排障实录（本阶段收尾时踩的坑）

> 这三个是 Phase 5 接线后**运行时**才暴露的问题，编译全过。学习价值极高：安全代码"编译通过 ≠ 能跑"。

### 排障 1 · `No DefaultChallengeScheme found`

- **现象**：启动后访问任意 `[Authorize]` 端点，抛 `InvalidOperationException: No authenticationScheme was specified, and there was no DefaultChallengeScheme found`。
- **根因**：`AddAuthentication()` 空配置，注册了 Bearer + ApiKey 却无默认方案；`EnforceAuthentication=true` 时 challenge 找不到方案。
- **修复**：见知识点 1，加 `Smart` policy scheme。
- **口诀**：**多方案认证报 "no DefaultChallengeScheme" → 加 policy scheme 或指定默认方案**。

### 排障 2 · Swagger 没有"模拟登录"

- **现象**：认证接上后，Swagger UI 里没有 Authorize 按钮，无法测受保护端点。
- **根因**：`AddSwaggerGen` 没有任何 `AddSecurityDefinition`，UI 自然不渲染锁图标。
- **修复**：
  1. Swagger + Scalar 双双补 `Bearer` 安全定义（Scalar 用 `AddOpenApi().AddDocumentTransformer`）；
  2. 新增 dev 登录端点 `POST /api/dev/login`，用**与 JwtBearer 相同的 issuer/audience/key** 签发 JWT（默认种子租户 + Admin）；
  3. **端点用 `DevLoginEnabled` 门控、默认 false**（主 appsettings=false，Development=true），避免生产变成"任意发 token"漏洞。
- **隐藏陷阱（bearer 双前缀）**：`type: http, scheme: bearer` 的 Authorize 弹窗会**自动加 `Bearer ` 前缀**，所以登录端点必须返回**裸 token**，否则变成 `Bearer Bearer xxx` 校验失败。
- **口诀**：**Swagger 无 Authorize 按钮 → 缺 AddSecurityDefinition；测试用 token 一律返回裸串**。

### 排障 3 · `no such table: AgentConfigurations`（EnsureCreated vs Migrate 反模式）

- **现象**：`SqliteException: no such table: AgentConfigurations`（以及 `ApiKeys`/`AuditLogs`）。
- **根因**：`DatabaseInitializer` 用了 `EnsureCreatedAsync()`，而项目**同时有 EF 迁移**（Phase2/3/5）。`EnsureCreated` 只在 DB 文件不存在时一次性建表；旧 DB 文件（早于 Phase3/5 迁移创建、无 `__EFMigrationsHistory`）**缺 3 张后加的表**。
- **额外发现**：模型相对最后一次迁移有**未提交变更**（`ApiKeys` 索引调整）。EF 默认对 pending model change 抛异常，运行时 `MigrateAsync` 同样会抛。
- **修复**：
  1. `DatabaseInitializer` 改用 `MigrateAsync()`（先 `GetPendingMigrationsAsync` 判空，catch 兜底 `EnsureCreated` 以兼容 InMemory 测试）；
  2. 用 `dotnet-ef` 补落缺失迁移 `Phase5ApiKeyIndex`（记得补 `#pragma warning disable IDE0161` 以过 `EnforceCodeStyleInBuild`）；
  3. 删掉陈旧 `agent_platform.db`（先 `.bak` 备份），`database update` 干净重建。
- **口诀**：**`no such table` 但模型有该实体 → 查 EnsureCreated/Migrate 混用；改模型必补 `migrations add`**。

---

## 10.5 提炼的 6 条安全工程原则

1. **默认拒绝（fail-closed）** —— 认证/授权/质量闸的缺省态是拒绝，放行必须是显式的有意识决策（呼应 Phase 4 的 fail-loud）。
2. **最小攻击面** —— 危险能力（dev 登录、公开 Key 端点）用开关门控、默认关闭；生产环境零调试后门。
3. **隔离要纵深** —— 多租户 = DB Query Filter + TenantProvider 解析 + 认证来源，三层缺一不可。
4. **凭证即身份来源** —— 角色/租户从可信凭证的 claim 取，绝不在代码里写死 Admin/默认租户。
5. **加密用 AEAD + 密钥外置** —— API Key 用 AES-256-GCM，密钥生产必须环境变量覆盖，硬编码 = 没加密。
6. **安全代码"编译通过 ≠ 能跑"** —— 认证/迁移这类问题多在运行时才暴露；接线后必须启动实测（本阶段三个排障都是编译全过、运行才炸）。

---

## 10.6 自检清单（可对照代码验证）

- [ ] `Program.cs` 认证块有 `Smart` policy scheme + `ForwardDefaultSelector`，非空 `AddAuthentication()`
- [ ] `ApiKeyAuthenticationHandler` 无头返回 `NoResult()`，无效才 `Fail()`
- [ ] `TenantProvider` 是 Scoped，从 claim 解析 `tenant_id`，非恒返回默认
- [ ] `GetRoles` 从凭证取真实角色，非恒 Admin
- [ ] `ApiKey` 密文入库（`ApiKeyEncryptionService` + `AesGcmEncryptor`），`Rotate/Revoke` 有真实调用点
- [ ] `PromptInjectionService` 正则收窄且有负向测试
- [ ] `AuditLog` 覆盖业务 4 handler + Key 三点位
- [ ] `DatabaseInitializer` 用 `MigrateAsync`（非 `EnsureCreatedAsync`），无 pending model change
- [ ] Swagger/Scalar 有 Authorize 按钮，dev 登录端点 `DevLoginEnabled` 门控且返回裸 token
- [ ] `dotnet build` 0 warnings；`dotnet test` 103/103 passed

---

## 复盘自测

- 多方案认证（JWT + ApiKey）为什么要用 policy scheme？`[Authorize]` 不指定方案时依赖什么？
- 多租户隔离为什么"只建 DB Query Filter"不够？TenantProvider 为什么必须是 Scoped？
- API Key 为什么用 AES-256-GCM 而不是普通 AES？dev 密钥为什么必须生产覆盖？
- `no such table` 但模型里明明有该实体，根因通常是什么？改了模型忘了什么会导致 pending model change 抛异常？
- Swagger 的 bearer 输入框为什么要返回裸 token？

---

## 10.7 按能力查因（速查表）

| 能力 | 最容易踩的坑（名不副实的表现） | 怎么验证真落地 | 代码落点 |
|------|-------------------------------|---------------|----------|
| ① 认证多方案 | 无默认方案 → challenge 抛异常；handler 不适用时错误 `Fail` | 启动实测 `[Authorize]` 返回 401 而非 500；policy scheme 分发正确 | `Program.cs` / `ApiKeyAuthenticationHandler.cs` |
| ② 真实多租户 | `TenantProvider` 恒返回默认租户 | 不同 token 查到不同租户数据 | `TenantProvider.cs` / `AppDbContext.cs` |
| ③ RBAC | `GetRoles` 恒 Admin | Viewer token 访问 Admin 端点 → 403 | `ApiKeyAuthenticationHandler.cs` |
| ④ Key 加密+生命周期 | 明文入库；`Rotate/Revoke` 死代码 | DB 列是密文；ExpiryJob 有调用点 | `AesGcmEncryptor.cs` / `ApiKey.cs` / `ApiKeyExpiryJob.cs` |
| ⑤ 提示注入 | 正则过宽误杀 / 过窄漏防 | 负向测试正常输入不拦 | `PromptInjectionService.cs` |
| ⑥ 审计 | 关键操作无留痕 | 建/删/Key 操作写 AuditLog | `AuditLog.cs` / `AuditLogRepository.cs` |
| ⑦ 限流 | 无限流或维度选错 | 超频返回 429 | `Program.cs`（RateLimiter） |

**记忆钩子**：Phase 5 全是"名词即承诺"的安全模块（认证/隔离/加密/审计），最容易"编译通过但名不副实"，必须走 `ddd-code-reviewer` 对抗式审查 + **运行时实测**。

---

## 10.8 参考代码

- `src/AgentPlatform.Api/Program.cs` — 认证块（Smart policy scheme）、Swagger 安全定义、dev 登录端点、限流
- `src/AgentPlatform.Infrastructure/Auth/ApiKeyAuthenticationHandler.cs` — API Key 认证 handler（NoResult/Fail 语义）
- `src/AgentPlatform.Infrastructure/Persistence/TenantProvider.cs` — per-request 租户解析
- `src/AgentPlatform.Infrastructure/Security/AesGcmEncryptor.cs` / `ApiKeyEncryptionService.cs` — AES-256-GCM 加密
- `src/AgentPlatform.Domain/Aggregates/ApiKeys/ApiKey.cs` — API Key 聚合（Rotate/Revoke）
- `src/AgentPlatform.Infrastructure/Jobs/ApiKeyExpiryJob.cs` — 过期扫描 BackgroundService
- `src/AgentPlatform.Api/Middleware/PromptInjectionMiddleware.cs` / `Infrastructure/Security/PromptInjectionService.cs` — 提示注入防护
- `src/AgentPlatform.Domain/Aggregates/AuditLogs/AuditLog.cs` — 审计聚合
- `src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs` — MigrateAsync 初始化
- `phases/phase-5-security-hardening.md` — 验收标准 + 二次评审闭环记录
