# 11. 面试问答宝典：把项目亮点讲清楚

> 目标：针对项目中最常被追问的技术决策，准备好「一句话理由 + 代码落点」的回答。面试官问任何一题，你能在 30 秒内说出核心逻辑、选型权衡和工程教训。

---

## 11.1 架构决策类

### Q1 · Policy Scheme 解决了什么问题？为什么不用写死默认方案做 JWT/API-Key 并存？

**一句话理由**：ASP.NET Core 的 `AddAuthentication()` 只允许**一个默认方案**，但我们需要同时支持 JWT（`Authorization` 头）和 API-Key（`X-API-Key` 头）两种凭证，且让 `[Authorize]` 不指定方案也能正确分发。

**核心代码**（`AuthConfiguration.cs`）：

```csharp
options.DefaultScheme = "Smart";
.AddPolicyScheme("Smart", "Smart", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (context.Request.Headers.ContainsKey("Authorization"))
            return "Bearer";
        if (!string.IsNullOrEmpty(context.Request.Headers[apiKeyHeaderName].FirstOrDefault()))
            return "ApiKey";
        return "Bearer";  // 无凭证时按 Bearer challenge，返回 401 而非 500
    };
})
.AddJwtBearer("Bearer", ...)
.AddScheme<..., ApiKeyAuthenticationHandler>("ApiKey", null);
```

**为什么不是写死 Bearer 再单独接 ApiKey？**
- 写死 Bearer 作为默认方案 → API-Key 凭证发来时不会自动触发 ApiKey handler，需要在每个 `[Authorize]` 上额外指定。
- 写死 ApiKey 作为默认方案 → JWT 凭证同理。
- Policy scheme 是 ASP.NET Core 内置的**分发器**，它自己不做认证，只按规则把请求转发给具体方案——零额外依赖、零样板代码。

**工程教训**：多方案认证报 `No DefaultChallengeScheme found` → 检查 `AddAuthentication()` 是否空配置。见 Phase 5 排障实录一。

**代码落点**：`src/AgentPlatform.Api/Configuration/AuthConfiguration.cs` L27–61

---

### Q2 · DomainEventBus 为什么从 Application 层搬到 Infrastructure 层？DDD 铁律是什么？

**一句话理由**：Application 层定义**接口**（契约），Infrastructure 层做**实现**（怎么发消息）。把 `DomainEventBus` 留在 Application 层违反了 DDD 的依赖方向规则。

**铁律**：Domain `→` Application `→` Infrastructure
- Domain：零外部依赖，纯业务逻辑
- Application：定义抽象接口（`IDomainEventBus`），编排业务流程
- Infrastructure：实现接口（用 MediatR 的 `IPublisher` 发领域事件通知）

**改造前的问题**：`DomainEventBus` 实现直接 `new Mediator()` 或引用 `IMediator`——Application 层有了 Infrastructure 的依赖，破坏了"Application 层不能依赖 Infrastructure"的规则。

**改造后的结构**：

```
Application/Abstractions/IDomainEventBus.cs   ← 接口契约（stay）
Application/EventHandlers/DomainEventBus.cs    ← 被清空，留迁移标记
Infrastructure/Persistence/DomainEventBus.cs   ← 真正实现（搬到这里）
```

**代码落点**：
- 接口：`src/AgentPlatform.Application/Abstractions/IDomainEventBus.cs`
- 实现：`src/AgentPlatform.Infrastructure/Persistence/DomainEventBus.cs`
- 迁移标记：`src/AgentPlatform.Application/EventHandlers/DomainEventBus.cs`（内容仅一行注释指向 Infrastructure 的实现）
- DI 注册：`src/AgentPlatform.Infrastructure/DependencyInjection.cs`

**工程教训**：DDD 的依赖方向是**编译时约束**，不是运行时约定——违反它意味着 Application 层项目引用了 Infrastructure，单元测试将被迫引用基础设施，架构测试也通不过。

---

### Q3 · `ICommand<T>` 标记接口解决什么问题？不加会怎样？

**一句话理由**：让 `UnitOfWorkBehavior` 精确判断**哪些 MediatR 请求需要自动 SaveChanges**——只有写操作（Command）需要，查询（Query）不需要。

**核心代码**（`ICommand.cs`）：

```csharp
public interface ICommand<out TResponse> : IRequest<TResponse> { }
public interface ICommand : IRequest { }
```

**不加 `ICommand<T>` 会怎样？**
- 如果没有区分，UnitOfWorkBehavior 无法约束泛型参数，必须用 `where TRequest : IRequest<TResponse>`——所有 MediatR 请求（包括 Query）都会被拦截并触发 SaveChanges。
- Query 触发 SaveChanges 意味着：读取数据的同时可能误写库，且事务范围过大拖慢性能。

**加了之后的行为**（`UnitOfWorkBehavior.cs`）：

```csharp
// 约束让 UnitOfWorkBehavior 只拦截 Command，Query 走自己的干净管道
internal sealed class UnitOfWorkBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>   // ← 关键约束
{
    public async Task<TResponse> Handle(...)
    {
        var response = await next();
        // 1. 收集领域事件
        var aggregates = _unitOfWork.GetTrackedAggregates();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        // 2. 先提交事务（让事件处理器读到已提交数据）
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        // 3. 再分发领域事件
        foreach (var domainEvent in events)
            await _eventBus.PublishAsync(domainEvent, cancellationToken);
        // 4. 清空已分发的领域事件
        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();
        return response;
    }
}
```

**注意事项**：PipelineBehavior 按注册顺序执行，UnitOfWorkBehavior 必须在外层（先提交事务再发事件），确保领域事件处理器读到的已经是持久化后的数据。

**代码落点**：`src/AgentPlatform.Application/Abstractions/ICommand.cs`；`src/AgentPlatform.Application/Behaviors/UnitOfWorkBehavior.cs`

---

### Q4 · OrchestrationPrimitive 从 636 行拆成三个类，拆分依据是什么？

**一句话理由**：单一职责——OrchestrationPrimitive 之前既是**门面**（路由编排预设）、又是**执行者**（每一步的 Agent 调用）、还是**协商逻辑**（Agent 间对话路由）。三种职责变化原因不同，拆开后每个类只有一个理由改变。

**拆分前**：
- God class：636 行，混合了三步逻辑（路由→编排→协商），内聚性差
- 问题：改顺序逻辑可能影响协商逻辑，改协商可能误触顺序

**拆分后**：

| 类 | 职责 | 行数 | 变化原因 |
|----|------|------|---------|
| `OrchestrationPrimitive` | 门面 + 生命周期管理（Run/Pause/Resume/Retry） | ~300 | 工作流生命周期变化（加新操作、改暂停策略） |
| `SequentialOrchestrator` | 顺序管线执行（每步一个 Agent 串行调用） | ~180 | Agent 调用方式变化（改重试、改超时、改工具调用） |
| `NegotiationOrchestrator` | 多 Agent 协商（议价/辩论式输出） | ~180 | 协商算法变化（改轮数、改汇总策略） |

**依赖关系**：OrchestrationPrimitive `new` 两个内部 Orchestrator，通过依赖注入传入共享依赖（`IWorkflowRepository`、`IDomainEventBus` 等）。

**关键设计细节**：`OrchestrationPrimitive` 存储了 `s_resolvedPresets`（静态 `ConcurrentDictionary`）以记住每次 RunAsync 选择的预设（Sequential/Negotiation），确保 Resume 和 Retry 使用**一致的预设**，不会因 Context 变化"漂移"到不同的编排策略。

**代码落点**：
- `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs`
- `src/AgentPlatform.Infrastructure/Workflows/SequentialOrchestrator.cs`
- `src/AgentPlatform.Infrastructure/Workflows/NegotiationOrchestrator.cs`
- 测试：`src/AgentPlatform.Application.Tests/Workflows/OrchestrationPrimitiveTests.cs`

---

## 11.2 安全类

### Q5 · AES-256-GCM 加密 API Key 时，密钥怎么管理？轮换策略？

**一句话理由**：API Key 用 AES-256-GCM 认证加密（非纯加密），密钥通过 `Security:AesEncryptionKey` 配置注入。生产环境必须用环境变量/密钥管理服务覆盖，**绝不用硬编码**。

**加密方案**（`AesGcmEncryptor.cs`）：

```csharp
// 12 字节随机 nonce + 数据加密 + 16 字节认证标签 → 组合后 hex 输出
var nonce = RandomNumberGenerator.GetBytes(12);
using var aesGcm = new AesGcm(_key, 16);  // 128-bit tag
aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
// 输出格式: nonce(12) || ciphertext(N) || tag(16) → hex string
```

**密钥管理**：
- **开发环境**：`appsettings.Development.json` 中 64 字符 hex key（占位值）
- **生产环境**：通过环境变量 `Security__AesEncryptionKey` 或密钥管理服务（如 Azure Key Vault / AWS KMS / 内部密钥服务）注入
- **校验**：构造函数中检查 key 长度是否恰好 32 字节（64 hex chars），否则启动即抛异常（fail-fast）

**轮换策略**：
- `ApiKey.Rotate()`：聚合根方法，生成新 key 并加密，同时记录 `RotatedAt` 时间戳
- `ApiKeyExpiryJob`（BackgroundService，每 6h 扫描）：做过期提醒 / 清理，不做自动轮换——轮换由管理员显式触发（安全原则）
- 解密失败处理：`AesGcmEncryptor.Decrypt()` 在 GCM 认证失败时 `catch CryptographicException` 警告日志后 rethrow——**不会返回错误数据（fail-closed）**

**工程教训**：
- GCM（AEAD）比 CBC 更安全：自带完整性校验，防止密文被篡改后仍能解出垃圾
- 密钥硬编码入库 = 没加密——`git grep AesEncryptionKey` 查不到真实密钥才合格

**代码落点**：
- `src/AgentPlatform.Infrastructure/Security/AesGcmEncryptor.cs`
- `src/AgentPlatform.Infrastructure/Security/ApiKeyEncryptionService.cs`
- `src/AgentPlatform.Domain/Aggregates/ApiKeys/ApiKey.cs`（`Rotate()` / `Revoke()`）
- `src/AgentPlatform.Infrastructure/Jobs/ApiKeyExpiryJob.cs`

---

### Q6 · 提示注入防护是怎么做的？中间件逻辑？

**一句话理由**：`PromptInjectionMiddleware` 拦截 POST/PUT JSON 请求，用 `PromptInjectionService` 的**收窄正则**扫描 body，命中已知注入模式即拒绝（400），未命中则继续。正则经过负向测试，确保正常业务文本不误杀。

**中间件流程**（`PromptInjectionMiddleware.cs`）：

```
请求 → 只看 POST/PUT + application/json → 检查 ContentLength (≤100KB) →
读取 body → 恢复流位置 → PromptInjectionService.SanitizeUserMessage(body) →
    命中注入模式 → 返回 400 + "potentially dangerous content detected"
    未命中 → await _next(context)
```

**四种注入模式**（`PromptInjectionService.cs`）：

| 模式 | 正则（收窄后） | 拦截示例 |
|------|---------------|---------|
| 忽略指令 | `ignore (all )?previous (instructions\|prompts\|directions)` | "ignore all previous instructions" |
| 系统覆盖 | `you are (not )?(an? )?(AI )?(assistant\|chatbot\|model\|system)` | "you are not an AI assistant" |
| 角色冒充 | `(system\|user\|assistant)\s*:.*` | "user: ignore the prompt above" |
| 分隔符逃逸 | `<\|im_(start\|end)\|>`, `<<sys>>`, `[inst]` 等 | 对话模板标记注入 |

**特别说明**：`DelimiterBreakoutPattern()` 经过**大幅收窄**——从粗暴匹配括号/JSON 内容改为只匹配真正的"模板分隔符"形态（`<|im_start|>`、`<<sys>>`、`[inst]` 等），避免了误杀含 JSON 或代码块的正常请求。

**负向测试覆盖**：`PromptInjectionServiceTests.cs` 中正常输入如"请忽略排序，按时间""你是一个好助手"等不拦截。

**代码落点**：
- `src/AgentPlatform.Api/Middleware/PromptInjectionMiddleware.cs`
- `src/AgentPlatform.Infrastructure/Security/PromptInjectionService.cs`
- `src/AgentPlatform.Application.Tests/Security/PromptInjectionServiceTests.cs`

---

### Q7 · RateLimiter 配置了什么策略？

**一句话理由**：两个维度的令牌桶（Token Bucket）限流——按租户（100/min）和按 API Key（50/min），超限返回 429。

**配置**（`InfrastructureConfiguration.cs`）：

```csharp
services.AddRateLimiter(options =>
{
    // 按租户限流：per-tenant, 100请求/分钟
    options.AddPolicy("PerTenant", context =>
    {
        var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(tenantId, _ => new()
        {
            TokenLimit = 100,
            TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    // 按 API Key 限流：per-key, 50请求/分钟
    options.AddPolicy("PerApiKey", context => {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault() ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(apiKey, _ => new()
        {
            TokenLimit = 50,
            TokensPerPeriod = 50,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    options.RejectionStatusCode = 429;
});
```

**设计考量**：
- 按租户/Key 而非按 IP：IP 太粗（NAT 后多用户共享同 IP），租户/Key 才是业务隔离粒度
- Token Bucket 而非 Fixed Window：允许短时突发（100 tokens 可一次性用完然后等待），比固定窗口更符合 API 调用模式
- QueueLimit = 0：不排队，超限直接拒绝（fail-closed），避免积压拖垮资源

**代码落点**：`src/AgentPlatform.Api/Configuration/InfrastructureConfiguration.cs` L26–52

---

## 11.3 多租户与基础设施

### Q8 · 多租户隔离是"只建 DB Query Filter"吗？还需要什么？

**一句话理由**：隔离要**纵深三层**——数据库 Query Filter（兜底）+ TenantProvider 解析（中间层）+ 认证来源（入口）。只建 Query Filter 而 `TenantProvider` 恒返回默认租户，等于没隔离。

**三层架构**：

```
认证（凭证来源）
  ↓ tenant_id claim
TenantProvider（Scoped，per-request 解析 claim）
  ↓ tenant_id
EF Core Global Query Filter（对所有 ITenantScoped 实体自动 WHERE）
```

**为什么只建 DB 层不够？**
- Phase 4 结束时：`AppDbContext.OnModelCreating` 已经对所有 `ITenantScoped` 实体加了 `HasQueryFilter(e => e.TenantId == _tenantProvider.GetTenantId())`
- 但 `TenantProvider` 早期实现是硬编码 `return _settings.DefaultTenantId`——所有请求都是同一个租户，Query Filter 形同虚设
- Phase 5 修复：TenantProvider 改为从 `HttpContext.User.FindFirst("tenant_id")?.Value` 解析真实的 `tenant_id`

**为什么 TenantProvider 必须是 Scoped？**
- Singleton：第一个请求的租户会在整个进程中固定，后续请求复用错误租户
- Scoped：跟随 HTTP 请求生命周期，每个请求独立解析

**代码落点**：
- `src/AgentPlatform.Infrastructure/Persistence/TenantProvider.cs`（Scoped + 从 claim 解析）
- `src/AgentPlatform.Application/Abstractions/ITenantProvider.cs`
- `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`（`HasQueryFilter`）
- 测试验证：不同 token 查询到不同租户数据（SpecFlow `MultiTenantIsolation` 场景）

---

### Q9 · Redis 连接失败怎么降级到内存？降级后数据一致性怎么处理？

**一句话理由**：`RedisShortTermMemory` 在 Redis 不可用时（`RedisConnectionException`/`TimeoutException`）自动降级到 `ConcurrentDictionary` 本地缓存，上层无感知。降级是尽力而为（best-effort），不保证持久性。

**降级流程**（`RedisShortTermMemory.cs`）：

```csharp
try
{
    var db = _connection.GetDatabase();
    await db.StringSetAsync(prefixedKey, json, expiry.Value);  // 正常走 Redis
}
catch (RedisConnectionException ex)
{
    _redisFailed = true;
    _logger.LogWarning(ex, "Redis unreachable; falling back to in-memory cache");
    StoreFallback(prefixedKey, value, expiry);  // 降级到 ConcurrentDictionary
}
```

**Redis 恢复后的行为**：
- `SetAsync` 成功写 Redis 后，检查 `_redisFailed` 标志并清除：
  ```csharp
  if (_redisFailed) { _redisFailed = false; logger... "Redis connection restored"; }
  ```
- 降级期间写入的 `_fallbackMemory` 数据**不会被自动同步到 Redis**（重新上线后，Redis 里是旧数据，fallback 里有新数据）

**数据一致性处理**：
- **不保证严格一致**——降级是 best-effort，不是 distributed cache HA 方案
- 业务侧接受"短暂降级期间数据可能丢失/不同步"
- TTL 过期：不管是 Redis 还是 fallback，过期数据都会被清除（fallback 有独立过期检查）

**两个实现对比**：

| 实现 | 位置 | 用途 |
|------|------|------|
| `RedisShortTermMemory` | 生产主存储 | Redis 可用时用分布式缓存 |
| `InMemoryShortTermMemory` | QuickStart/单机 | 无外部依赖时直接使用（跳过 Redis） |

**代码落点**：
- `src/AgentPlatform.Infrastructure/Cache/RedisShortTermMemory.cs`
- `src/AgentPlatform.Infrastructure/Cache/InMemoryShortTermMemory.cs`

---

## 11.4 质量治理与工程化

### Q10 · 三道质量门禁怎么串联的？提交门禁怎么拦截？

**一句话理由**：三道质量门 + 一道设计评审关，通过 `.quality-gate.json` 标记 + Git hooks + CI Job 实现自动化拦截，形成"动手前审范式 → 动手后审实现 → 结构卫生 → 全库健康/生产就绪"的闭环。

**四道关卡**：

| 关 | 时机 | Skill | 卡什么 |
|----|------|-------|--------|
| 设计评审 ⭐ | 阶段启动时 | `blueprint-architecture-review` | 范式正确性（无 P0/P1 阻断） |
| ① 对抗审查 | 高风险模块合入前 | `ddd-code-reviewer` | 代码是否照蓝图做、核对章节 |
| ② 结构门禁 | 高风险模块合入前 | `ddd-phase-quality-gate` | DDD 卫生（DI/EF/分层/并发/密封） |
| ③ 全库健康 | 阶段完成时 | `codebase-optimizer` | 8 维度全库扫描（含桩代码替换进度） |

**提交拦截机制**：

```
git commit (含 src/ 改动)
  → pre-commit hook: 校验 .quality-gate.json 已暂存
                      → cleared: true
                      → codebaseOptimizer 字段存在
  → commit-msg hook: 校验 message 含 "Quality-Gate: phase-X cleared (...)"
  → CI (push/PR): quality-gate job 在服务器端重复校验
```

**质量标记格式**（`.quality-gate.json`）：

```json
{
  "phase": "p0-workflow-update-endpoint",
  "reviewer": "ddd-code-reviewer PASSED",
  "structureGate": "ddd-phase-quality-gate PASS (P0=0 P1=0 P2=0 P3=0)",
  "codebaseOptimizer": "frontend QA pipeline PASSED",
  "cleared": true,
  "reportRef": "docs/quality/p0-workflow-update-gate.md"
}
```

**工程要点**：
- 三重拦截：本地 hook（pre-commit + commit-msg） + CI（quality-gate job），双保险
- 只拦 `src/` 改动，`docs/` 等不受限
- 标记是**人为维护的声明**——钩子信任它，所以必须诚实

**代码落点**：
- 钩子实现：`scripts/git-hooks/pre-commit`、`scripts/git-hooks/commit-msg`
- 安装脚本：`scripts/install-hooks.ps1`
- CI：`.github/workflows/ci.yml`（`quality-gate` job）
- 规范文档：`docs/quality/QUALITY-GATE.md`

---

### Q11 · codebase-optimizer 跑了两轮发现了什么？修了什么？

**一句话理由**：两轮扫描覆盖**【架构 → 代码质量 → 正确性 → 测试 → 性能 → 安全 → 工程化 → 生产就绪度】**八维度，第一轮定位 11 个问题（含 1 个严重：TODO 死锁漏锁 + 无超时），第二轮确认 7/7 已修，安全/工程化维度清零，生产就绪度 8 维度 5 已到位 3 待补。

**第一轮发现（Round 1，11 tasks）**：

| 严重度 | 问题 | 修复 |
|--------|------|------|
| 🔴 严重 | `VolatileAgentCoordinator` 的 TODO 漏锁 + 无超时（行锁变表锁死锁风险） | 补 `SemaphoreSlim` + 超时 + 异步锁释放 |
| 🟡 中 | `StateMachineEngine` 回调事件不用 `async`，异常捕获 "String" 而非 `Exception` | 统一异常类型，补 async 处理 |
| 🟡 中 | `ToolCallingOrchestrator` 无 cancellation 传播 | 补 `ct.ThrowIfCancellationRequested()` |
| 🟡 中 | `ModelClient` 无超时传播 | 用 `CancellationTokenSource.CreateLinkedTokenSource` 设总体超时 |
| 🟡 中 | `IRetryPolicy` 接口已定义但未实现 | 实现 RetryPolicy + Polly 集成 |
| 🟢 低 | 数处 `List<T>` 应改为 `IReadOnlyList<T>` | 按最小暴露原则改 |
| 🟢 低 | `Debug.WriteLine` 遗留 | 移除 |
| 🟢 低 | 架构测试 `CommandProjection_ShouldNotReferenceInfrastructure` 漏配 | 补 CommandProjection 命名空间 |
| 🟢 低 | Infrastructure.Tests 未加架构测试 | 补齐 DDD 架构约束 |
| 🟢 低 | `RaceConditionFacts` 锁测试遗漏 | 补充锁语义测试 |
| 🟢 低 | 桩函数 `StateMachineEngine` 未使用 | 确认桩代码下不影响运行 |

**第二轮结果（Round 2，5 维度全覆盖）**：
- **性能**：async I/O 全到位、无阻塞调用、锁边界清晰
- **安全**：所有 `CancellationToken` 传播到位、超时策略落地
- **工程化**：build 0 warnings、test 143/143 passed、`IReadOnlyList<T>` 最小暴露
- **桩代码替换进度**：`StubModelClient` 正常使用中（蓝图明确允许 Phase 5 保留 Stub）
- **生产就绪度**：8 维度 5 已到位（认证/隔离/日志/测试/可观测），3 待补（健康检查/优雅关闭/metrics dashboard）

**代码落点**：`.codebase-optimizer/` 目录下完整报告；`docs/learning/08-decision-log.md` §8.11.

---

### Q12 · 项目整体架构是什么样的？DDD + Clean Architecture 各层职责？

**一句话理由**：四层 DDD（Domain → Application → Infrastructure → Api）+ MediatR CQRS + 依赖方向严格向内，Domain 零外部依赖。

**项目结构**：

```
AgentPlatform.Domain          ← 零 PackageReference，纯 C#
  ├ Aggregates/               ← 聚合根（Workflow, ApiKey, AuditLog, AgentType...）
  ├ Abstractions/             ← IDomainEvent, IRepository<T>...
  ├ Enums/                    ← AgentType, WorkflowState...
  └ ValueObjects/             ← 值对象（record）

AgentPlatform.Application     ← 只引用 Domain + MediatR
  ├ Abstractions/             ← 接口定义（IDomainEventBus, IAesEncryptor...）
  ├ Behaviors/                ← 管道行为（UnitOfWorkBehavior）
  ├ **/Commands/              ← CQRS Command + Handler
  ├ **/Queries/               ← CQRS Query + Handler
  └ **/EventHandlers/         ← 领域事件处理器

AgentPlatform.Infrastructure  ← 引用 Application
  ├ Persistence/              ← EF Core DbContext, UnitOfWork, Repositories
  ├ Security/                 ← AesGcmEncryptor, PromptInjectionService
  ├ Auth/                     ← ApiKeyAuthenticationHandler
  ├ Cache/                    ← RedisShortTermMemory, InMemoryShortTermMemory
  ├ Workflows/                ← OrchestrationPrimitive, SequentialOrchestrator
  └ DependencyInjection.cs    ← DI 注册（只暴露给 Api 层调用）

AgentPlatform.Api             ← ASP.NET Core Web API
  ├ Controllers/              ← REST 端点
  ├ Middleware/                ← PromptInjectionMiddleware
  ├ Configuration/            ← AuthConfiguration, InfrastructureConfiguration
  └ Program.cs                ← 启动入口
```

**依赖方向铁律**：
```
Domain → Application → Infrastructure → Api
                         ↓
               (Infrastructure 实现 Application 的接口)
```

- Domain **不能引用** Application 或 Infrastructure
- Application **不能引用** Infrastructure（接口定义在 Application，实现在 Infrastructure）
- Infrastructure **引用** Application（实现接口）
- Api **引用** Infrastructure（调用 `AddInfrastructureDI()`）

**架构测试验证**：`ArchUnitNET` 测试在 `Infrastructure.Tests` 中自动校验上述依赖方向，**编译时不通过无法提交**。

---

## 11.5 工程实践类

### Q13 · 哪个决策你最后悔？如果重来会怎么做？

**真实教训：`EnsureCreated` 与 EF Migration 混用（Phase 5 排障三）**

**问题**：`DatabaseInitializer` 使用 `EnsureCreatedAsync()` 而非 `MigrateAsync()`，而项目同时有 EF Core 迁移（Phase 2/3/5）。旧数据库文件（早于 Phase 3 创建、无 `__EFMigrationsHistory` 表）缺少后加的 `ApiKeys`、`AuditLogs` 等表，运行时抛 `no such table: AgentConfigurations`。

**根因**：`EnsureCreated` 只在数据库文件**不存在时**一次性建表，后续模型变更不会更新已有数据库。而 `MigrateAsync` 基于迁移历史逐步执行。

**修复**：
1. `DatabaseInitializer` 改为 `MigrateAsync()`（先 `GetPendingMigrationsAsync` 判空，catch 兜底 `EnsureCreated` 以兼容 InMemory 测试）
2. 补落缺失迁移 `Phase5ApiKeyIndex`
3. 删旧 `agent_platform.db` 重建

**教训**：但凡项目用到 EF 迁移，数据库初始化必须用 `Migrate()`/`MigrateAsync()`，绝不能用 `EnsureCreated`。后者只适合原型阶段或纯内存测试。

**代码落点**：`src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs`

---

### Q14 · 143 个测试怎么分布的？架构测试测什么？

**测试分层**：

| 层级 | 框架 | 数量 | 测什么 |
|------|------|------|--------|
| 单元测试（Application） | xUnit + Moq | ~65 | Handler/Behavior/Domain 逻辑 |
| 单元测试（Infrastructure） | xUnit + 真实 EF InMemory | ~37 | Repository/Encryption/Security |
| 架构测试 | ArchUnitNET | ~12 | DDD 依赖方向（Domain→Application→Infrastructure） |
| 集成测试 | xUnit + Testcontainers | ~14 | Redis/数据库/API 端点 |
| BDD 验收 | SpecFlow + xUnit | 41 场景 | 工作流/路由/多租户/CustomAgent/ExecutionLog |

**架构测试具体覆盖**（`Infrastructure.Tests`）：

```csharp
// 示例：Domain 层不能引用任何外部包
[Fact]
void DomainLayer_ShouldNotReferenceInfrastructure()
{
    var domainLayer = Types.InCurrentAssembly()
        .That().ResideInNamespace("AgentPlatform.Domain")
        .And().AreNotInterfaces();
    var result = domainLayer.Should()
        .NotDependOnAny(Assemblies.InfrastructureAssembly)
        .Check();
    Assert.True(result.IsSuccessful);
}

// 更多约束：
// - Application 不能引用 Infrastructure
// - Command Handler 命名空间符合 CQRS 模式
// - Query Handler 不能调用 SaveChanges
```

**为什么架构测试在底层？**
- 架构测试运行最快（毫秒级），在 CI 的第一层拦截违规
- 违反 DDD 依赖方向意味着编译时间接引用不需要的包，是**系统性错误**，不是业务错误
- 架构测试失败时，不会浪费后续单元/集成测试的运行时间

---

### Q15 · CI/CD 管道怎么设计的？质量门禁怎么在 CI 中实现？

**CI 管道（`.github/workflows/ci.yml`）**：

```
触发: push/PR 到 master（含 src/ 改动）
  ↓
quality-gate job（并行）        build-test job（并行）
  ├ 校验 .quality-gate.json       ├ dotnet build
  ├ 校验 cleared=true              ├ dotnet test (143 tests)
  └ 校验 codebaseOptimizer 存在    └ quality-gate job 等待的结果
                      ↓
           合并判断：两者都通过 → 绿色
                    任一失败 → 红色
```

**CI 中的质量门禁**：
- 独立 `quality-gate` job（与 `build-test` 并行）
- 校验 `.quality-gate.json` 文件存在且格式正确
- 校验 `id: phase-X` 与当前分支匹配
- 校验 `cleared: true` + `codebaseOptimizer` 字段存在
- 与本地 hooks 互补：本地拦遗漏，CI 拦绕过 hooks 的提交

**快速启动**：`QuickStart` launch profile 零外部依赖（SQLite + Stub 模型），无需 Docker/Redis/数据库配置，`dotnet run --launch-profile QuickStart` 即可启动。

---

## 附录 · 面试应答速查表

| 面试官问 | 一句话核心 | 代码落点 |
|---------|-----------|---------|
| 怎么同时支持 JWT 和 API-Key？ | Policy Scheme 做分发器，按请求头分发 | `AuthConfiguration.cs` L27–61 |
| DomainEventBus 为什么搬了？ | DDD 铁律：Application 定义接口，Infrastructure 实现 | `IDomainEventBus.cs` → `Persistence/DomainEventBus.cs` |
| `ICommand<T>` 有什么用？ | 区分读写，让 UnitOfWorkBehavior 只拦截写操作 | `ICommand.cs` + `UnitOfWorkBehavior.cs` |
| 636 行的类怎么拆的？ | 门面/顺序编排/协商，三种职责变化原因不同 | `OrchestrationPrimitive.cs` → 3 文件 |
| API Key 怎么加密？ | AES-256-GCM，nonce + ciphertext + tag 组合 hex 输出 | `AesGcmEncryptor.cs` |
| 提示注入怎么防？ | 中间件拦截 POST/PUT，收窄正则匹配 4 种注入模式 | `PromptInjectionMiddleware.cs` + `PromptInjectionService.cs` |
| 多租户怎么隔离？ | 纵深三层：认证 → TenantProvider → Query Filter | `TenantProvider.cs` + `AppDbContext.cs` |
| Redis 挂了怎么办？ | 自动降级到 ConcurrentDictionary，恢复后自动切回 | `RedisShortTermMemory.cs` |
| 质量门禁怎么实现的？ | `.quality-gate.json` + hooks + CI，三层拦截 | `QUALITY-GATE.md` + `pre-commit` + `ci.yml` |
| 143 个测试够全面吗？ | 架构测试兜底 + BDD 验行为 + 集成验真依赖，分层互补 | `05-testing-strategy.md` |
| 项目中哪个决策最后悔？ | `EnsureCreated` 混用 Migration，运行时 `no such table` | `DatabaseInitializer.cs` |

---

> **使用建议**：面试前把每道题的"一句话理由"背熟，30 秒说清核心。面试官追问细节时，有把握的展开讲代码落点和工程教训，没把握的诚实说"这个当时是团队决策/标准实现，我可以查文档复述"。
