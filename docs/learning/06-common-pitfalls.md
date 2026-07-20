# 06. 常见踩坑汇总（Phase 1 实战记录）

> 目标：这些坑你一定会踩，提前知道省一天时间。所有记录来自 phase-1-baseline-mvp.md 踩坑表。

> **一句话**：26 个真实踩坑 + 一句诊断口诀，编译错/运行炸/数据写不进/并发不准/环境不对时先翻这篇。

---

## 6.0 按症状查因（报错先翻这张）

> 不知道从哪查时，先用口诀（§6.8）定位大方向，再用下表精确到坑号。

| 你遇到的症状 | 先查方向（口诀） | 对应坑 |
|--------------|------------------|--------|
| 编译报错：API / 类型找不到 | 查版本号 & using | #1 AddMediatR、#2 FinishReason、#3 IChatCompletionService、#4 Scalar using |
| 编译报错：类型歧义 / 重复定义 | 同名类型 / 枚举定义 | #5 AgentRole、#6 MessageRole、#8 RoutingPolicy 两份 |
| 运行时炸：服务不生效 | 查 DI 注册 | #7 IToolRegistry、#13 Decorator、#15 仓储未生效 |
| 数据写不进 / 映射异常 | 查 EF Core 映射 | #9 Messages、#10 OwnsMany、#11 列名、#12 提供者选错、#16 IOptions 默认 |
| 并发不准 / 偶发炸 | 查 lock + 线程安全 | #14 `_todaySpent`、#21 decimal、#22 Dictionary、#23 跨天不重置 |
| 环境 / 配置不对 | 查 launch-profile & 配置 | #17 `--configuration`、#18 HTTPS、#19 Scalar 不显示、#20 TenantId |
| 弹性管道 / 重试异常 | 查 Polly 用法 | #24 非泛型、#25 层暴露、#26 未使用 |
| 架构违规（编译不报） | 查 ArchitectureTests | 教训见 #5/#6/#8，用架构测试自动拦截 |

---

## 6.1 NuGet / 版本相关

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 1 | `AddMediatR` 编译找不到 | 项目装了 MediatR v11，代码用了 v12 API | 开新阶段前先锁版本：`dotnet list package`，`PackageReference` 写死版本号 |
| 2 | SK `FinishReason` 属性编译报错 | 1.12 有 `FinishReason`，1.30 已移除 | SK 1.12 到 1.30 有 breaking changes。升级前查 release notes |
| 3 | `IChatCompletionService` 命名空间找不到 | 1.12 在 `Microsoft.SemanticKernel`，1.30 移至 `.ChatCompletion` | 版本锁定后，第一时间验证 API 签名 |
| 4 | `Scalar.AspNetCore` 编译报错 | 忘了 `using Scalar.AspNetCore;` | NuGet 包的 README 会告诉你需要什么 using |

**教训：** 不要相信 `dotnet add package` 的默认版本。**显式锁定版本号。**

---

## 6.2 DDD / 架构相关

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 5 | `AgentRole` 编译报歧义错误 | Domain.Aggregates.Agents 里值对象 record 和 Domain.Enums 枚举同名 | 聚合根子目录下不要定义和全局枚举同名的类型 |
| 6 | `MessageRole` 两处独立定义 | 脚手架生成时 Application 和 Domain 各写了一份 | 统一的枚举只放在 Domain/Enums/ |
| 7 | `IToolRegistry` 未注册 DI | 写了接口和实现，忘了 `AddScoped` | **检查清单：** 每个新接口加完后检查 DI 注册 |
| 8 | `RoutingPolicyDomainService` 有两份 | Application 和 Domain 各有一个 | 领域服务只放在 Domain，Application 只编排 |

**教训：** 架构违规通常编译不报错。用 ArchitectureTests 自动检查。

---

## 6.3 EF Core 映射

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 9 | `Conversation.Messages` 写入就报异常 | `IReadOnlyList<Message>` 接口不可写 | 集合用 `UsePropertyAccessMode(PropertyAccessMode.Field)` |
| 10 | `OwnsMany` 主键冲突 | 影子 Id 没配 `ValueGeneratedOnAdd()` | 建配置类时检查每条影子属性 |
| 11 | `TokenUsage` 列名冲突 | `Conversation` 和 `Message` 都有 `TotalTokenUsage` | 值对象列一律用 `.HasColumnName()` 显式指定 |
| 12 | SQLite 提示 Npgsql 提供者错误 | `ConnectionStrings:PostgreSQL` 用了 `Data Source=` 前缀 | DI 里按前缀自动选 `UseSqlite` / `UseNpgsql` |

**教训：** 每加一个聚合根，写一个 `IEntityTypeConfiguration`，而不是依赖 EF Core 约定。

---

## 6.4 DI / 生命周期

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 13 | `ModelTelemetryDecorator` 注册了但不生效 | .NET DI 没有内置装饰器支持 | 自注册 + 工厂方法包裹装饰器 |
| 14 | `CostController` 丢数据 | `_todaySpent += cost` 不是原子操作 | Singleton 的可变状态必须 `lock` |
| 15 | 仓储在 DI 容器注册了但没生效 | 写了 `AddScoped` 但 handler 注入了错误接口 | 写测试验证：`serviceProvider.GetService<IAgentRepository>()` 不为 null |
| 16 | `IOptions<T>` 注入值全是默认 | 忘了在 `Program.cs` 写 `services.Configure<T>(...)` | 检查 Checklist |

**教训：** DI 注册后第一时间写一个空测试验证解析成功。

---

## 6.5 ASP.NET Core / 配置

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 17 | `--configuration QuickStart` 环境没生效 | `--configuration` 只影响编译配置（Debug/Release），不影响环境 | 用 `--launch-profile QuickStart` |
| 18 | HTTPS 无限重定向 | `UseHttpsRedirection` 后没有 HTTPS 端点 | 条件化：仅当有 HTTPS endpoint 时启用 |
| 19 | Scalar 在 QuickStart 不显示 | `app.MapScalarApiReference()` 包在 `if (IsDevelopment())` 里 | 改成 `if (!IsProduction())` 或无条件 |
| 20 | `TenantId` 全是同一个 GUID | 开发初期写死了 `Guid.Parse` | 用 `IOptions<TenantSettings>` + appsettings |

**教训：** ASP.NET Core 的环境机制（Development/Staging/Production/QuickStart）容易混淆，理解 `--launch-profile` 和 `--configuration` 的区别。

---

## 6.6 并发 / 线程安全

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 21 | `decimal` 累加不准 | 两个请求同时 `+=` | `decimal` 不支持 `Interlocked`，必须 `lock` |
| 22 | `Dictionary` 在 Singleton 里炸了 | 并发读写 | 用 `ConcurrentDictionary` |
| 23 | `CostController._todaySpent` 跨天不重置 | 没有按天重置逻辑 | 每日首次请求自动重置，配置化 `DailyBudget` |

**教训：** 任何 Singleton 服务只要有可写字段，假设它会被并发访问。

---

## 6.7 Polly / Resilience

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 24 | `ResiliencePipeline` 非泛型无法直接返回 T | Polly 8.x 非泛型的 `ExecuteAsync` 返回 `ValueTask` | 用闭包捕获 result |
| 25 | `ResiliencePipelineProvider` 在 Application 无法引用 | Polly 类型暴露到了 Application 层 | 接口包装后放到 `Application.Abstractions` |
| 26 | 注册了 `IResiliencePipelineProvider` 但 ModelRouter 没用 | ModelRouter 自己写 try/catch | 建立检查清单：新服务要求必须使用弹性管道 |

---

## 6.8 快速诊断口诀

```
编译报了错 → 查版本号
运行期炸了 → 查 DI 注册
数据写不进去 → 查 EF Core 映射
并发不准 → 查 lock + ConcurrentDictionary
环境不对 → 查 launch-profile + --configuration
跨天不重置 → 查 Singleton 状态重置逻辑
```

---

## 复盘自测

- 编译报错、运行期炸、数据写不进、并发不准、环境不对，分别先查什么？（背出口诀）
- 架构违规为什么编译不报错？靠什么拦截？
- 为什么 DI 注册后要第一时间写个空测试验证解析成功？

---

## 参考代码

- `phases/phase-1-baseline-mvp.md` 的完整踩坑表（346 行）
- `Directory.Build.props`（TreatWarningsAsErrors = true）
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
