# F41 · 移除 QuickStart 模式、强制真实 Key 与环境变量配置

> **优先级**：P1（阻断当前 Bug 根因）  
> **风险**：高（涉及启动流程、DI 注册、测试工厂、CI 配置、文档）  
> **设计文档**：本文件即为设计文档，实现前需确认无异议
>
> **状态**：✅ **已实现**（2026-08-26，commit `a11a6c6`；平台模型配置 DB 化 follow-up `62ede44`；后续 CI 环境变量映射与 E2E 隔离修复见 CHANGELOG v2.33）

---

## 1. 背景与动机

**当前问题**：QuickStart 模式（`ASPNETCORE_ENVIRONMENT=QuickStart`）注册 `StubModelClient` 作为平台模型，但**未替换 `ITenantModelClientResolver`**。若租户库里有 BYO 凭据（如前期手动添加的 openai/ox-alpha），`ModelRouter` 会把 BYO 候选排在平台模型前，导致显式传 `model: "stub"` 也被忽略，走真实 OpenAI → 403 `insufficient_user_quota`（即本次 Bug 根因）。

**设计目标**：
1. **彻底移除 QuickStart** —— 不再提供「零外部依赖」的一键体验模式
2. **正常启动强制真实 Key** —— `Development` / `Production` / `Staging` 均走 `SemanticKernelModelClient`；无 `OpenAI:Key` / `OpenAI:BaseUrl` 配置时，启动直接抛异常（fail-fast），不再静默回退 Stub。DeepSeek/vLLM 均兼容 OpenAI 协议，统一走 OpenAI 配置。
3. **测试用真实 Key** —— 单元/集成/BDD 测试均从环境变量读取真实 Key（CI 配置 `OPENAI_API_KEY` 等），尽量测出潜在问题
4. **API 从环境变量加载默认配置** —— `OpenAI:Key`、`OpenAI:Model`、`OpenAI:BaseUrl` 等从环境变量映射，无需 `appsettings.json` 硬编码

---

## 2. 变更范围

| 文件/模块 | 变更类型 | 说明 |
|-----------|----------|------|
| `src/AgentPlatform.Api/Properties/launchSettings.json` | 删除 | 移除 `QuickStart` profile |
| `src/AgentPlatform.Api/Program.cs` | 修改 | 移除 `IsEnvironment("QuickStart")` 判断；启动时校验至少一个真实模型 Key |
| `src/AgentPlatform.Infrastructure/DependencyInjection.cs` | 修改 | 移除 `isTestEnv` 包含 QuickStart 的逻辑；仅 `Test` 环境注册 `StubModelClient`；其余环境强制 `SemanticKernelModelClient` 并校验 Key |
| `src/AgentPlatform.SpecFlowTests/IntegrationAppFactory.cs` | 修改 | 从环境变量读取真实 Key 注入配置；移除 `ModelClient:Provider=Stub` |
| `src/AgentPlatform.Api.Tests/ApiContractTestFactory.cs` | 修改 | 单元测试保留 Stub（不变），集成测试改用真实 Key |
| `src/AgentPlatform.SpecFlowTests/StubTenantModelClientResolver.cs` | 删除 | 不再需要（测试层隔离改为环境变量真实 Key） |
| `.github/workflows/ci.yml` | 修改 | CI 注入 `OPENAI_API_KEY` 等 Secret |
| `README.md` | 修改 | 移除 QuickStart 启动说明，改为环境变量配置真实 Key |
| `docs/AGENT_PLATFORM_BLUEPRINT.md` | 修改 | 同步架构文档 |
| 各阶段设计文档 | 修改 | 清理 QuickStart 相关引用 |

---

## 3. 详细设计

### 3.1 启动校验逻辑（Program.cs）

```csharp
// 新增：启动时强制校验真实 OpenAI 兼容 provider
var llmConfigured = !string.IsNullOrEmpty(configuration["OpenAI:Key"])
    || !string.IsNullOrEmpty(configuration["OpenAI:BaseUrl"]);

if (!llmConfigured && !app.Environment.IsEnvironment("Test"))
{
    throw new InvalidOperationException(
        "No LLM provider configured. Set OpenAI:Key (env OPENAI_API_KEY) " +
        "and optionally OpenAI:BaseUrl (env OPENAI_BASE_URL) for OpenAI/DeepSeek/vLLM (all OpenAI-compatible). " +
        "Test environment is exempt and uses StubModelClient.");
}
```

### 3.2 DI 注册逻辑（DependencyInjection.cs）

```csharp
// 仅 Test 环境允许 StubModelClient
var isTestEnv = environment.IsEnvironment("Test");
var modelProvider = configuration.GetSection("ModelClient:Provider").Value;

if (string.Equals(modelProvider, "Stub", StringComparison.Ordinal) && isTestEnv)
{
    // Test 环境显式配置 Provider=Stub 时才注册 Stub
    services.AddScoped<IModelClient>(_ => new StubModelClient(...));
}
else
{
    // Development / Production / Staging / Integration 均走真实模型
    // 启动时已在 Program.cs 校验 Key 存在
    services.AddScoped<SemanticKernelModelClient>();
    services.AddScoped<IModelClient>(sp => 
        new ModelTelemetryDecorator(sp.GetRequiredService<SemanticKernelModelClient>(), ...));
}
```

### 3.3 环境变量映射

约定：环境变量优先级高于 `appsettings.json`

| 环境变量 | 映射配置键 | 说明 |
|----------|------------|------|
| `OPENAI_API_KEY` | `OpenAI:Key` | 必填 |
| `OPENAI_MODEL` | `OpenAI:Model` | 可选，默认 `gpt-4o-mini` |
| `OPENAI_BASE_URL` | `OpenAI:BaseUrl` | 可选，默认官方；指向 DeepSeek/vLLM 端点即可复用 |

> **DeepSeek/vLLM 兼容 OpenAI 协议**：只需设置 `OPENAI_BASE_URL` 指向对应服务的 `/v1` 端点，无需单独配置 Key。

在 `Program.cs` 早期通过 `configuration.AddEnvironmentVariables()` 自动映射（ASP.NET Core 默认已启用）。

### 3.4 测试工厂配置真实 Key

**IntegrationAppFactory**：
```csharp
// 从环境变量读取，CI 必须提供
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var deepSeekKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
var vllmUrl = Environment.GetEnvironmentVariable("VLLM_BASE_URL");

if (string.IsNullOrEmpty(openAiKey) && string.IsNullOrEmpty(deepSeekKey) && string.IsNullOrEmpty(vllmUrl))
    throw new InvalidOperationException("CI must provide at least one LLM API key via env vars");

IntegrationConfiguration["OpenAI:Key"] = openAiKey ?? "";
IntegrationConfiguration["DeepSeek:Key"] = deepSeekKey ?? "";
IntegrationConfiguration["VLLM:Url"] = vllmUrl ?? "";
// 移除 ModelClient:Provider=Stub
```

**ApiContractTestFactory**：保留 `Test` 环境 + `ModelClient:Provider=Stub` + `StubTenantModelClientResolver`（单元测试不联网，维持现状）。

### 3.5 CI 配置（.github/workflows/ci.yml）

```yaml
env:
  OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
  DEEPSEEK_API_KEY: ${{ secrets.DEEPSEEK_API_KEY }}
  VLLM_BASE_URL: ${{ secrets.VLLM_BASE_URL }}
```

GitHub 仓库需预置 `OPENAI_API_KEY` Secret（最低额度即可跑测试）。

---

## 4. 验收标准

| # | 验收项 | 通过条件 |
|---|--------|----------|
| 1 | `launchSettings.json` 无 QuickStart profile | `dotnet run` 仅剩 `http` / `IIS Express` |
| 2 | `Development` 启动无 Key 抛异常 | `dotnet run` 报 "No LLM provider configured" |
| 3 | `Development` 启动有 Key 正常 | 设置 `OPENAI_API_KEY` 后 `dotnet run` 成功监听 5000 |
| 4 | `Test` 环境单元测试仍用 Stub | `dotnet test --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~SpecFlowTests"` 全绿 |
| 5 | `Integration` 环境 BDD 用真实 Key | CI `integration` job 全绿，日志显示真实模型调用 |
| 6 | 前端 E2E 能跑通真实模型 | CI `frontend-e2e` job 全绿 |
| 7 | 文档已同步更新 | README / BLUEPRINT 无 QuickStart 残留 |

---

## 5. 实施顺序

1. **设计评审关** —— 本文档确认无异议后进入编码
2. **后端核心改动**（并行可做）：
   - 删除 launchSettings.json QuickStart profile
   - Program.cs 启动校验 + 移除 QuickStart 判断
   - DependencyInjection.cs 仅 Test 环境允许 Stub
3. **测试工厂改动**：
   - IntegrationAppFactory 注入真实 Key（环境变量）
   - 删除 StubTenantModelClientResolver.cs
4. **CI 配置**：添加 Secret 注入
5. **文档同步**：README、BLUEPRINT、各阶段设计文档
6. **全量回归**：`dotnet build` + `dotnet test` + 前端 E2E 全绿
7. **质量门禁**：跑 `ddd-code-reviewer` + `ddd-phase-quality-gate` + `codebase-optimizer` 至 0 open findings

---

## 6. 影响评估

| 维度 | 影响 | 缓解 |
|------|------|------|
| 新人上手 | 需配置 Key | README 提供最小 Key 申请指引 + CI Secret 配置示例 |
| 本地开发 | 需本地 `.env` 或环境变量 | 提供 `.env.example` 模板 |
| CI 成本 | 真实模型调用产生费用 | 使用最小额度模型（`gpt-4o-mini`），单次测试 < $0.01 |
| 破坏性变更 | 现有 QuickStart 用户直接受影响 | 版本升级到 v3.0.0，CHANGELOG 标注 BREAKING CHANGE |

---

## 7. 回滚方案

如发现不可接受的回归：
1. 恢复 `launchSettings.json` QuickStart profile
2. DependencyInjection.cs 恢复 `isTestEnv` 包含 QuickStart
3. Program.cs 恢复 QuickStart 环境判断
4. 文档回滚
5. 版本号不升级，直接 `git revert` 相关 commits