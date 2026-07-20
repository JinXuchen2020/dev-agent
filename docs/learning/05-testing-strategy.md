# 05. 测试策略：ArchTests → BDD → Unit → Integration

> 目标：理解这个项目的测试金字塔为什么这样搭，每种测试解决什么问题，不解决什么问题。

---

## 5.1 测试金字塔（当前状态）

```
         ┌──────────┐
         │   E2E    │  ← 没有。Phase 5 补
         │ (手动)   │
        ┌┴──────────┴┐
        │ Integration │  ← Testcontainers 脚手架就绪，测试用例待 Phase 2
        │ (少量)     │
       ┌┴────────────┴┐
       │  BDD SpecFlow │  ← 17 个场景。验证业务行为
       │ (中量)       │
      ┌┴───────────────┴┐
      │ Unit (xUnit)    │  ← 没有独立的 Unit Test 项目
      │ (少量)          │     业务逻辑靠 BDD 步骤覆盖
     ┌┴─────────────────┴┐
     │ ArchitectureTests  │  ← 6 个测试，每次 build 自动跑
     │ (编译级约束)       │
     └───────────────────┘
```

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

## 5.4 Unit Test：当前缺口

目前这个项目**没有独立的 Unit Test 项目**。业务代码的测试覆盖率来自 BDD 步骤里间接执行的代码。

### 什么时候需要 Unit Test？

```csharp
// 复杂逻辑：CostController 的每日预算逻辑
public bool CanAffordAsync(Money cost)
{
    ResetDailyIfNeeded();
    return _todaySpent + cost <= _dailyBudget;
}
```

BDD 场景验证了"成本报表返回正确花费"，但以下边界情况 BDD 没覆盖：

- 跨天重置：23:59:59 花了一笔，00:00:00 再花，预算重置了吗？
- 并发：两个请求同时 `CanAffordAsync`，都返回 true 但预算只够一个
- 货币不匹配：USD + CNY 应该抛异常

这些需要 Unit Test，不需要数据库，不需要启动 API。

### 建议

Phase 2 有复杂业务逻辑（状态机、Agent 协作）时加 Unit Test 项目：

```
src/AgentPlatform.Application.Tests/
├── Routing/
│   └── CostControllerTests.cs
├── Workflows/
│   └── StateMachineTests.cs
└── Agents/
    └── AgentTypeTests.cs
```

---

## 5.5 Integration Test：验证真实基础设施

### 当前状态

Testcontainers 脚手架已经搭好：

- `PostgreSqlContainerFixture` — 启动 real PostgreSQL 16
- `RedisContainerFixture` — 启动 real Redis 7
- `HealthCheckIntegrationTest` — 验证 API 能启动、/health 返回 200

### 什么时候写 Integration Test？

当 Phase 2 把 Stub 替换成真实实现时：

```
Stub → 真实实现             需要 Integration Test？
─────────────────────       ──────────────────────
PgVectorStore Stub          → PGVector 全文检索真的能召回文档？
RedisShortTermMemory        → Redis 断连时能优雅降级到内存？
DockerCodeSandbox Stub      → 沙箱容器真的限制 CPU/内存？
AutoGenAgentOrchestrator    → 多个 Agent 真的能协作完成对话？
```

**规则：** 所有涉及外部依赖（DB、Redis、Docker、外部 API）的代码，都必须有一个 Integration Test 验证它在真实依赖下的行为。

### CI 中的集成测试

```yaml
# .github/workflows/ci.yml
- name: Run integration tests
  if: false  # 需要 Docker 环境的 runner 时改成 true
  run: dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

集成测试需要 Docker，所以默认 CI 跳过。当你有 Docker runner 时，开启就行。

---

## 5.6 各种测试的 ROI 对比

| 测试类型 | 编写成本 | 执行速度 | 发现问题类型 | 建议数量级 |
|---------|---------|---------|------------|-----------|
| ArchitectureTests | 低（搭一次） | < 1s | 架构违规，DI 遗漏 | 6-10 个 |
| Unit Test | 中 | < 10ms | 逻辑错误，边界条件 | 核心逻辑全覆盖 |
| BDD SpecFlow | 中高 | < 5s | 业务行为偏差 | 每阶段 5-10 个场景 |
| Integration | 高 | 10s+ | 基础设施交互错误 | 每个外部依赖 2-3 个 |

**结论：** ArchitectureTests 是投入产出比最高的 — 写一次，每次 build 自动执行，永远不修。

---

## 参考代码

- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
- `src/AgentPlatform.SpecFlowTests/Features/AgentRouting.feature`
- `src/AgentPlatform.SpecFlowTests/Steps/AgentRoutingSteps.cs`
- `src/AgentPlatform.IntegrationTests/PostgreSqlContainerFixture.cs`
- `src/AgentPlatform.IntegrationTests/HealthCheckIntegrationTest.cs`
- `.github/workflows/ci.yml`
