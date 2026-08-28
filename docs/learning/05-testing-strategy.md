# 05. 测试策略：ArchTests → BDD → Unit → Integration

> 目标：理解这个项目的测试金字塔为什么这样搭，每种测试解决什么问题，不解决什么问题。

> **一句话**：测试金字塔最底层是 ArchitectureTests——它补的是「C# 编译器不管、但架构会烂」的缺口，ROI 最高。

---

## 5.1 测试金字塔（2026-08-28 现状）

```
         ┌──────────────────┐
         │   E2E (真浏览器)  │  ← playwright-bdd 27 场景（真实 key 起真后端）
        ┌┴──────────────────┴┐
        │  BDD 集成 (真HTTP)  │  ← Reqnroll 114 场景，真 HTTP + 文件 SQLite，
        │  (Integration 环境) │     Integration 环境强制真实 LLM Key（F41）
       ┌┴────────────────────┴┐
       │  Unit (xUnit)        │  ← App226 / Infra154+6skip / Api35 / Arch9 全绿
       │  (大量)              │
      ┌┴───────────────────────┴┐
      │ ArchitectureTests       │  ← 9 个测试，每次 build 自动跑
      │ (编译级约束)            │
      └─────────────────────────┘
```

> **关键演进（F41 起）**：集成测试与 E2E **不再用 Stub 模型**——CI 注入 `OPENAI_API_KEY`，BDD/E2E 打真实 LLM（OpenAI/DeepSeek/vLLM 任一兼容端点）；`ModelClient:Provider=Stub` 仅 `Test` 单元测试环境生效。用户拍板：「e2e 就是要用真实 key，不要用 stub」。

## 5.2 为什么 ArchitectureTests 在最底层

传统项目只有 Unit → Integration → E2E。但 DDD 项目的架构违规**编译能通过**（C# 编译器不管依赖方向）。

```csharp
// 编译 ✅ 通过，架构 ❌ 违规
// Application 层引用 Infrastructure
using AgentPlatform.Infrastructure;
```

ArchitectureTests 补了这个缺口：

```csharp
[Fact]
public void Application_Should_NotReference_Infrastructure()
{
    var content = File.ReadAllText("AgentPlatform.Application.csproj");
    Assert.DoesNotContain("AgentPlatform.Infrastructure", content);
}
```

**每次 `dotnet test` 执行，6 秒跑完，0 个假阳性。** 如果有人不小心在 Application.csproj 加了 Infrastructure 引用，PR 直接红。

---

## 5.3 BDD（SpecFlow）：验证业务行为，不是代码

### 一个 BDD 场景

```gherkin
Scenario Outline: 主模型超时后降级到备用模型
  Given 主模型 "<Primary>" 调用超时
  When 路由层触发降级策略
  Then 应使用备用模型 "<Fallback>" 重试

  Examples:
  | Primary   | Fallback  |
  | gpt-4o    | deepseek  |
  | deepseek  | gpt-4o    |
```

**BDD 解决了什么问题？**

- 场景是中文的，产品和测试也能看懂
- `Scenario Outline` + `Examples`，一组测试覆盖 N 条降级链路
- 测试名直接是业务行为，不是 `TestMethod_WhenX_ShouldY`

**BDD 不解决什么问题？**

- 不验证边界条件（null 参数、空集合、并发竞争）— 需要 Unit Test
- 不验证真实基础设施（Redis 断连、PostgreSQL 超时）— 需要 Integration Test

---

## 5.4 Unit Test：现状

单元测试体系已全面建立（Phase 2 起补齐）：

- `AgentPlatform.Application.Tests`（226）— 路由/编排器/成本/评估门禁等核心逻辑
- `AgentPlatform.Infrastructure.Tests`（154 + 6 skip）— EF 迁移/沙箱/工具执行器（Docker 类用 `[SkippableFact]`，本机无 daemon 跳过）
- `AgentPlatform.Api.Tests`（35）— 契约测试（`Test` 环境 + Stub）
- `AgentPlatform.ArchitectureTests`（9）— 分层约束

经验：**测试工程必须纳入 `AgentPlatform.sln`**，否则 `dotnet test src/AgentPlatform.sln` 会漏跑（曾踩坑）。

---

## 5.5 Integration Test：验证真实基础设施

### 当前状态

- Reqnroll BDD 基座：`IntegrationAppFactory`（WebApplicationFactory + 真 HTTP + 文件 SQLite + 种子租户/ApiKey），8 功能域 114 场景全绿
- Docker 类集成测试：`PostgreSqlContainerFixture` / `RedisContainerFixture`，CI 有 daemon 的 runner 上跑，本机无 daemon 用 `[SkippableFact]` 跳过
- **F41 后：Integration 环境强制真实 LLM Key**——`IntegrationAppFactory` 从环境变量读 `OPENAI_API_KEY` 注入配置，缺 Key 直接抛异常

### CI 中的集成测试

```yaml
# .github/workflows/ci.yml（已启用）
env:
  OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
```

集成 job 打真实 LLM（默认 `gpt-4o-mini`，可用 `OPENAI_BASE_URL` 指向 DeepSeek/vLLM）。**HttpClient 天花板已放宽至 5 分钟**（`Api.Timeout`），否则真实 LLM 冷启动/抖动下流式回复会被默认 100s 截断成 `TaskCanceledException`，看起来像 500。

---

## 5.6 真实 Key 测试的三大实战教训（2026-08-28）

1. **测试间租户状态污染**：credentials 场景创建的 BYO 假凭据（假 key + 空 BaseUrl→api.openai.com）留在默认租户，ModelRouter「BYO 优先」让**后续所有场景**的真实 LLM 调用全走必失败凭据 → 连环 401/500。修复 = 测试自清理副作用（场景末尾 DELETE 凭据）。排查「真实 LLM 相关 E2E 连环失败」时**先查租户状态污染，再查代码路径**。
2. **SQLite 不强制 varchar(n) 长度**：前端 E2E 后端用 SQLite，任何「列截断 → DbUpdateException → 500」的假设在该环境都不成立（对 Postgres 无害，对 SQLite 是死路）。
3. **VSTest 过滤器匹配中文场景名的陷阱**：`FullyQualifiedName~<中文>` 匹配 0 测试且 **exit 0**（假绿）——中文场景标题要用 `DisplayName~` 过滤。

### E2E 断言要等「终态」而非单一成功文案

真实 LLM 下运行可能多轮/超时/429——前端错误告警与成功文案是互斥渲染的。E2E 断言应该 `locator.or()` 等任一终态出现，错误先现时读出真实错误文本再失败，否则以「找不到元素」静默超时掩盖根因。

---

## 5.7 各种测试的 ROI 对比

| 测试类型 | 编写成本 | 执行速度 | 发现问题类型 | 建议数量级 |
|---------|---------|---------|------------|-----------|
| ArchitectureTests | 低（搭一次） | < 1s | 架构违规，DI 遗漏 | 6-10 个 |
| Unit Test | 中 | < 10ms | 逻辑错误，边界条件 | 核心逻辑全覆盖 |
| BDD 集成（Reqnroll） | 中高 | 秒级 | 业务行为偏差 + 路由/编排链路 | 每功能域 5-15 个场景 |
| E2E（playwright-bdd） | 高 | 分钟级 | 前后端契约、UI 渲染、真实 LLM 链路 | 每核心旅程 1-3 个场景 |

**结论：** ArchitectureTests 是投入产出比最高的 — 写一次，每次 build 自动执行，永远不修。E2E 最贵也最容易「假绿/假红」，断言必须等终态、测试必须自清理。

---

## 复盘自测

- 为什么 ArchitectureTests 要放在测试金字塔最底层？它补了什么编译器补不了的缺口？
- BDD 验证了什么、不验证什么？
- 什么情况下必须写 Integration Test 而不是 Unit/BDD？
- 为什么集成/E2E 要用真实 key？测试间污染是怎么发生的、如何防？

---

## 参考代码

- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
- `src/AgentPlatform.SpecFlowTests/Features/AgentRouting.feature`
- `src/AgentPlatform.SpecFlowTests/Steps/AgentRoutingSteps.cs`
- `src/AgentPlatform.IntegrationTests/PostgreSqlContainerFixture.cs`
- `src/AgentPlatform.IntegrationTests/HealthCheckIntegrationTest.cs`
- `.github/workflows/ci.yml`
