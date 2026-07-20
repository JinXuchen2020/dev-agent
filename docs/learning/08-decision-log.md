# 08. 关键决策记录

> 目标：记录项目中的关键决策——当时有什么选项，为什么选这个，后续影响是什么。

> **一句话**：每个关键选型都记了「当时有什么选项、为什么选这个、后续影响」，方便日后复盘「为什么这么干」。

---

## 8.1 模型路由策略

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 | 复杂度 |
|------|------|--------|
| **模型特定降级链** | 每个模型配置自己的降级链（如 gpt-4o → deepseek → qwen） | 高 |
| **Flat Priority List** | 一个全局排序列表，选最高优先级可用模型 | 低 |

### 选择：Flat Priority List

```json
{
  "Router": {
    "Candidates": [
      { "ModelId": "gpt-4o", "Provider": "Azure", "Priority": 1 },
      { "ModelId": "deepseek", "Provider": "OpenAI", "Priority": 2 }
    ]
  }
}
```

**理由：**
- Phase 1 只有少量模型，flat list 足够简单
- 配置驱动，添加新模型不需要改代码
- Phase 2 如果降级策略变复杂，可以加权重或成本路由，但接口（`IModelRouter`）不需要改

### 后续影响

- Phase 1 的 `ModelRouter` 重构过一次（从模型特定链改为 flat list），因为原始方案太复杂
- Phase 2 可能需要加成本权重路由，但 `ICostController` 接口已经预留

---

## 8.2 Domain 事件：适配器 vs 直接 MediatR

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 重构 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 |
|------|------|
| **Domain 直接依赖 MediatR** | 聚合根构造函数里 `mediator.Publish(...)` |
| **适配器模式** | Domain 定义 `IDomainEvent` 纯接口，Application 层桥接到 MediatR |

### 选择：适配器模式

```
Domain（零依赖）         Application（桥接层）           MediatR
    │                         │
    │  IDomainEventBus         │
    │  ────────────────────── >│
    │                          │  DomainEventBus
    │                          │  ─→ IPublisher.Publish()
```

**理由：**
- Domain 项目零外部依赖的铁律不能破
- 如果未来从 MediatR 换成 Brighter 或其他事件总线，Domain 层不改
- 代价是多了 2 个文件（`IDomainEventBus` + `DomainEventBus` 适配器）

### 后续影响

- Phase 1 重构了一次（从直接 MediatR 改为适配器）
- UnitOfWorkBehavior 负责从聚合根自动收集 `_domainEvents` 并分发
- 后续 Phase 加新事件时，不需要关心是哪个消息库在背后

---

## 8.3 Scalar vs Swagger UI

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 |
|------|------|
| **只用 Swagger UI** | Swashbuckle 自带，最通用 |
| **只用 Scalar** | .NET 9 原生 OpenAPI + Scalar UI，更现代 |
| **两个都要** | Swagger UI（/swagger）+ Scalar（/scalar/v1）都保留 |

### 选择：两个都要

**理由：**
- Swagger UI 是行业标准，外部开发者最熟悉
- Scalar UI 更现代、更美观，适合演示
- 两个路径不冲突，维护成本为零（都是配置行）

**后续调整：**
- 最初 Scalar 只在 Development 环境暴露 → 改为 `!IsProduction()` → 最终取消环境限制
- 默认 `launchUrl` 从 `openapi/v1.json`（原始 JSON）改为 `swagger`（Swagger UI 页面）

---

## 8.4 QuickStart 模式：SQLite + Stub

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 |
|------|------|
| **仅支持 PostgreSQL** | 所有环境必须启动 Docker + PostgreSQL |
| **SQLite + Stub** | QuickStart 用 SQLite + Stub 模型，0 外部依赖 |

### 选择：SQLite + Stub

**理由：**
- 新人上手成本：不需要 Docker、不用配 API Key、不消耗 token
- 10 个 Stub 组件确保 QuickStart 模式零外部依赖
- `dotnet run --launch-profile QuickStart` → 3 秒启动

### 后续影响

- 数据库连接串前缀检测（`Data Source=` → SQLite，其他 → Npgsql）
- appsettings.QuickStart.json 独立配置
- 所有新手文档和 README 里的命令都用 QuickStart

---

## 8.5 CostController 生命周期

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 |
| **决策者** | 架构组 |

### 选项

| 方案 | 问题 |
|------|------|
| **Scoped** | 每个请求创建新实例，_todaySpent 永远不累计 |
| **Singleton** | 所有请求共享，但必须处理并发 |

### 选择：Singleton + 线程安全

**理由：**
- 花费累计必须在所有请求间共享（Scoped 做不到）
- 需要 `lock` 保护 `_todaySpent` 读写
- 需要每日自动重置逻辑

**后续影响：**
- 加 `ICostController` 接口抽象，ModelRouter 通过接口引用
- 配置化 DailyBudget（从 `RouterSettings` 读取）
- 所有 `decimal` 加减操作加 `lock`

---

## 8.6 ICommand\<T\> 标记接口

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-09，Phase 1 重构 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 |
|------|------|
| **所有请求都走 UnitOfWork** | Command 和 Query 都触发 SaveChanges |
| **用标记接口区分** | 只有 ICommand\<T\> 触发 SaveChanges |

### 选择：ICommand\<T\> + 泛型约束

```csharp
public interface ICommand<TResponse> : IRequest<TResponse> { }

// Pipeline 只对 ICommand<TResponse> 生效
public class UnitOfWorkBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
```

**理由：**
- Query 不需要触发 SaveChanges（性能浪费）
- 泛型约束比运行时判断更安全（编译期保证）
- Phase 1 重构时查出的 bug：Query 也走了 SaveChanges，改了

### 后续影响

- 所有 Command 必须实现 `ICommand<T>`（不是 `IRequest<T>`）
- 忘记加标记 → Query 不会触发 UnitOfWork → 不会自动 SaveChanges → 数据不落库

---

## 8.7 决策演化时间线

```
2026-07-01 (v1.0 基线)
├── 选择 .NET 9 + SK 1.30 + MediatR 12
├── 选择 DDD 6 项目结构
└── 选择 SpecFlow BDD

2026-07-09 (Phase 1 实现)
├── 模型路由：先做模型特定降级链 → 后改为 Flat Priority List
├── Domain 事件：先直接用 MediatR → 后改为适配器模式
├── Scalar：仅 Development → !IsProduction() → 无条件
├── CostController：Scoped → Singleton + lock + 按天重置
├── UnitOfWork：所有请求都 SaveChanges → 只有 ICommand<T>
└── TenantId：硬编码 GUID → IOptions<TenantSettings>

2026-07-13 (Phase 1 收尾)
├── Swagger/Scalar：取消环境限制
├── launchUrl：openapi/v1.json → swagger
├── 加 ArchitectureTests（6 项 DDD 约束测试）
├── 加 IntegrationTests（Testcontainers 脚手架）
├── 加 CI workflow（+ --vulnerable 安全扫描）
└── 加学习文档（8 篇）
```

---

## 复盘自测

- 模型路由为什么选 Flat Priority List 而不是模型特定降级链？
- Domain 事件为什么用适配器模式桥接 MediatR，而不是直接依赖？
- `ICommand<T>` 标记接口的选型，后续影响是什么？

---

## 参考

- `AGENT_PLATFORM_BLUEPRINT.md` 修改日志（v1.0 ~ v1.5）
- `phases/phase-1-baseline-mvp.md` 的"已应用的重构"表（84 项）
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
