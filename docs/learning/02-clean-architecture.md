# 02. Clean Architecture 依赖方向：为什么是 6 个项目

> 目标：理解 "依赖方向向内" 到底是什么意思，为什么少写一个项目会出问题。

---

## 2.1 项目关系图

```
                  ┌─────────────────────────┐
                  │    AgentPlatform.Api     │  (表现层)
                  │  ASP.NET Core Web API    │
                  └──────┬──────────┬────────┘
                         │          │
                         ▼          ▼
              ┌──────────────────┐  ┌──────────────────────────────┐
              │ AgentPlatform    │  │  AgentPlatform                │
              │ .Application     │  │  .Infrastructure             │
              │ (用例编排)       │  │  (EF Core, SK, Redis, 等)    │
              └──────┬──────────┘  └──────────────────────────────┘
                     │
                     ▼
              ┌──────────────────┐
              │ AgentPlatform    │
              │ .Domain          │  (零外部依赖)
              └──────────────────┘

  AgentPlatform.Workflow     → Application (同层，Phase 2 填充)
  AgentPlatform.Web          → Api (前端项目，通过 HTTP 通信)
```

**引用方向（写在 .csproj 里的 `<ProjectReference>`）：**

```
Api        → Application, Infrastructure
Application → Domain
Infrastructure → Application
Workflow   → Application
Domain     → （无引用）
```

---

## 2.2 为什么 Domain 必须零依赖

### 看 AgentPlatform.Domain.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```

没有 `<PackageReference>`，没有 `<ProjectReference>`。这是刻意的。

**理由：**

| 场景 | 不加依赖 | 加了依赖会怎样 |
|------|---------|---------------|
| 把 ORM 从 EF Core 换成 Dapper | Domain 不动，只改 Infrastructure | 如果 `IAgentRepository` 返回了 EF Core 的 `IQueryable`，整个 Domain 被 EF Core 污染 |
| 把消息队列从 RabbitMQ 换成 Kafka | Domain 不动 | Domain 里多了 `IBus` 接口定义，消息基础设施的变更影响领域逻辑 |
| 给测试写 Unit Test | 不需要 mock 外部依赖 | 每次 new 一个聚合根需要初始化 EF Core DbContext |

**一个简单的测试验证：**

```csharp
// Domain 的 .csproj 不允许有任何 PackageReference
var content = File.ReadAllText("AgentPlatform.Domain.csproj");
Assert.DoesNotContain("<PackageReference", content);
```

这个测试就在 `AgentPlatform.ArchitectureTests/DddLayerTests.cs` 里，每次 `dotnet test` 都会跑。

---

## 2.3 铁律：接口定义在哪，实现在哪，注册在哪

| 层 | 职责 | 举例 |
|----|------|------|
| **Application.Abstractions** | 定义接口 | `IModelClient`, `IVectorStore`, `IToolExecutor` |
| **Infrastructure** | 实现接口 | `SemanticKernelModelClient`, `PgVectorStore`, `NativeToolExecutor` |
| **Infrastructure/DependencyInjection.cs** | 注册 DI | `services.AddScoped<IModelClient, SemanticKernelModelClient>()` |

### 常见错误：接口定义在 Infrastructure

```csharp
// ❌ Infrastructure/Abstractions/IModelClient.cs
// 坏处：Application 如果要引用 IModelClient，就不得不引用 Infrastructure
namespace AgentPlatform.Infrastructure.Abstractions;
public interface IModelClient { ... }

// Application/SomeHandler.cs
using AgentPlatform.Infrastructure.Abstractions; // ← Application 引用了 Infrastructure！
```

### 正确做法：接口在 Application，实现在 Infrastructure

```csharp
// ✅ Application/Abstractions/IModelClient.cs
namespace AgentPlatform.Application.Abstractions;
public interface IModelClient { ... }

// Infrastructure/Models/SemanticKernelModelClient.cs
namespace AgentPlatform.Infrastructure.Models;
internal sealed class SemanticKernelModelClient : IModelClient { ... }
```

Application 引用 `AgentPlatform.Application.Abstractions.IModelClient`，不引用 Infrastructure 里的任何东西。

---

## 2.4 为什么 Api 可以引用 Infrastructure，Application 不行

```
Api → Application ✅ 允许
Api → Infrastructure ✅ 允许（为了注册 DI）
Application → Infrastructure ❌ 禁止
```

**原因：** DI 注册需要知道具体实现类。`Program.cs` 里写：

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
```

`AddInfrastructure()` 里面注册了 `SemanticKernelModelClient`、`AgentRepository` 等具体实现。如果 Api 不引用 Infrastructure，就调不到 `AddInfrastructure()`。

**但是 Application **不能**引用 Infrastructure：**

```csharp
// ❌ Application/SomeHandler.cs
using AgentPlatform.Infrastructure; // 编译能过，架构违规
```

编译不报错，所以需要 ArchitectureTests 来拦截：

```csharp
var content = File.ReadAllText("AgentPlatform.Application.csproj");
Assert.DoesNotContain("AgentPlatform.Infrastructure", content);
```

---

## 2.5 违反依赖方向会怎样

| 违规 | 编译 | Runtime | 发现时机 |
|------|------|---------|---------|
| Domain 引用第三方包 | ✅ 通过 | 依赖地狱 | PR 审查 |
| Application 引用 Infrastructure | ✅ 通过 | ✅ 没问题 | ArchitectureTests 拦截 |
| Api 引用了 Domain | ✅ 通过 | ✅ 没问题 | 代码审查 |
| Infrastructure 定义了接口 | ✅ 通过 | ✅ 没问题 | ArchitectureTests 拦截 |

**关键点：** C# 编译器不阻止架构违规。所以需要 ArchitectureTests 来补这个缺口。这也是为什么第一个 todo 就是建 ArchitectureTests 项目。

---

## 2.6 这个结构的实际好处

你在这个项目里实际体验到的：

1. **改 EF Core 版本** → 只改 `AgentPlatform.Infrastructure.csproj`，Domain/Application 完全不动
2. **加新 ORM** → 新实现在 Infrastructure，实现已有接口就行
3. **换模型 Provider** → `StubModelClient` ↔ `SemanticKernelModelClient` 通过配置切换，代码一处不改
4. **写单元测试** → new 聚合根不需要数据库，不需要 mock

---

## 参考代码

- `src/AgentPlatform.Domain/AgentPlatform.Domain.csproj`（零依赖）
- `src/AgentPlatform.Application/AgentPlatform.Application.csproj`（只引用 Domain）
- `src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj`（引用 Application）
- `src/AgentPlatform.Api/AgentPlatform.Api.csproj`（引用 Application + Infrastructure）
- `src/AgentPlatform.Infrastructure/DependencyInjection.cs`（DI 注册中心）
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`（架构约束测试）
