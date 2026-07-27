# 06. 常见踩坑汇总（Phase 1 + Phase 5 实战记录）

> 目标：这些坑你一定会踩，提前知道省一天时间。#1~#26 来自 phase-1-baseline-mvp.md 踩坑表；#27~#31 来自 Phase 5 安全加固实战。

> **一句话**：31 个真实踩坑 + 一句诊断口诀，编译错/运行炸/数据写不进/并发不准/环境不对/认证炸/迁移缺表时先翻这篇。

---

## 6.0 按症状查因（报错先翻这张）

> 不知道从哪查时，先用口诀（§6.11）定位大方向，再用下表精确到坑号。

| 你遇到的症状 | 先查方向（口诀） | 对应坑 |
|--------------|------------------|--------|
| 编译报错：API / 类型找不到 | 查版本号 & using | #1 AddMediatR、#2 FinishReason、#3 IChatCompletionService、#4 Scalar using |
| 编译报错：类型歧义 / 重复定义 | 同名类型 / 枚举定义 | #5 AgentRole、#6 MessageRole、#8 RoutingPolicy 两份 |
| 运行时炸：服务不生效 | 查 DI 注册 | #7 IToolRegistry、#13 Decorator、#15 仓储未生效 |
| 数据写不进 / 映射异常 | 查 EF Core 映射 | #9 Messages、#10 OwnsMany、#11 列名、#12 提供者选错、#16 IOptions 默认 |
| 并发不准 / 偶发炸 | 查 lock + 线程安全 | #14 `_todaySpent`、#21 decimal、#22 Dictionary、#23 跨天不重置 |
| 环境 / 配置不对 | 查 launch-profile & 配置 | #17 `--configuration`、#18 HTTPS、#19 Scalar 不显示、#20 TenantId |
| 弹性管道 / 重试异常 | 查 Polly 用法 | #24 非泛型、#25 层暴露、#26 未使用 |
| 认证炸：no DefaultChallengeScheme | 查认证默认方案 | #27 多方案无默认、#28 handler 误 Fail |
| Swagger 无法测受保护端点 | 查 SecurityDefinition | #29 无 Authorize 按钮、#30 bearer 双前缀 |
| 运行时缺表 `no such table` | 查 EnsureCreated/Migrate 混用 | #31 迁移反模式 + pending model change |
| 列表页整页 ErrorState（某 GET 400） | 查控制器 take 校验早于 handler clamp | #32 take<1 控制器 400 早于 handler Math.Clamp |
| 角色被误标「自定义」/ 内建区恒空 | 查前端硬编码 code 与 DB 是否对齐 | #33 角色分类双源不一致（AgentType vs AgentRoleDefinition） |
| 设计文档「锁定决策」被用户推翻 | 查用户真实使用（文档决策可修订） | #34 设计决策非铁律 |
| curl 中文参数返回 400 | 查 Git Bash 编码（非后端 bug） | #35 命令行中文参数编码 |
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

## 6.8 认证 / 授权（Phase 5）

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 27 | 启动后访问 `[Authorize]` 抛 `No authenticationScheme was specified, and there was no DefaultChallengeScheme found` | `AddAuthentication()` 空配置，注册了多个方案却无默认方案；`EnforceAuthentication=true` 时 challenge 找不到方案 | 加 `Smart` policy scheme 作默认方案，`ForwardDefaultSelector` 按请求头分发到 Bearer/ApiKey |
| 28 | 带 JWT 的请求被 ApiKey handler 拒了 / 多方案互相短路 | handler 在自己不适用时返回了 `Fail()` 而非 `NoResult()` | 无 `X-API-Key` 头时返回 `NoResult()`（"交给别的方案"），有头但无效才 `Fail()` |

**教训：** 多方案认证必须有"选择器"。`[Authorize]` 不指定 `AuthenticationSchemes` 时完全依赖默认方案；handler 不适用时用 `NoResult()` 不要 `Fail()`。

---

## 6.9 Swagger / 测试凭证（Phase 5）

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 29 | 认证接上后 Swagger UI 没有 Authorize 按钮，无法测受保护端点 | `AddSwaggerGen` 没有任何 `AddSecurityDefinition` | 补 `Bearer` 安全定义 + 全局安全需求；Scalar 用 `AddOpenApi().AddDocumentTransformer` 同步 |
| 30 | 把签发的 token 贴进 Authorize 弹窗仍 401 | `scheme: bearer` 弹窗会**自动加 `Bearer ` 前缀**，返回带前缀的 token 就变成 `Bearer Bearer xxx` | 登录/发 token 端点一律返回**裸 token**；dev 登录端点用 `DevLoginEnabled` 门控、默认 false |

**教训：** Swagger bearer 输入框自动补前缀，测试用 token 一律返回裸串；调试后门（dev 登录）必须开关门控、生产默认关。

---

## 6.10 EF 迁移 / 数据库初始化（Phase 5）

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 31 | 运行时 `no such table: AgentConfigurations`（模型里明明有该实体） | `DatabaseInitializer` 用 `EnsureCreatedAsync()`，但项目同时有 EF 迁移。`EnsureCreated` 只在 DB 不存在时一次性建表；旧 DB（无 `__EFMigrationsHistory`）缺后加的表 | 改用 `MigrateAsync()`；改模型必须 `dotnet ef migrations add`（否则 pending model change 会抛异常）；本地删旧 DB 让迁移重建 |

**教训：** `EnsureCreated` 与 Migrations **不能混用**。有迁移就全程 `MigrateAsync`；新增实体/字段后忘了 `migrations add` 会静默导致 `no such table` 或 pending model change 抛异常。

---

## 6.11 快速诊断口诀

```
编译报了错 → 查版本号
运行期炸了 → 查 DI 注册
数据写不进去 → 查 EF Core 映射
并发不准 → 查 lock + ConcurrentDictionary
环境不对 → 查 launch-profile + --configuration
跨天不重置 → 查 Singleton 状态重置逻辑
认证 challenge 炸 → 查默认方案 / policy scheme
Swagger 没 Authorize → 查 AddSecurityDefinition
运行时缺表 → 查 EnsureCreated/Migrate 混用
```

---

## 6.13 2026-07-27 新增坑（Dashboards / 角色 / 流程）

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 32 | Dashboard 整页 ErrorState，Network 显示 `GET /api/v1/workflows?take=0` → 400 | 列表端点控制器层 `if (take < 1) return BadRequest` 早于 handler 内部的 `Math.Clamp(take,1,100)`；前端为取 `totalCount` 故意传 `take=0` → 被拒；且 Dashboard 用 `error = a||w||s||f` 的 OR 逻辑，3 个 `take=0` 请求全 400 → 整页错误态 | 只取计数用 `take=1`（handler 的 `totalCount` 由独立 COUNT 得出，与 take 无关）；要做 count-only 须同步改 Workflows/ExecutionLogs/AgentConfigurations 三处控制器 + handler + 单测（见 08 §8.13） |
| 33 | 系统架构/产品经理等平台默认角色被标成「自定义」，内建区恒空 | `AgentRoleDefinition`（DB，code=architecture/development/...）与 `AgentType`（代码值对象，code=architect/developer/...）两套 code 完全不互通；前端用硬编码 `BUILT_IN_ROLES=['architect',...]` 判定内建，对不上 DB code；聚合又无 `IsBuiltIn` 字段 | 统一以 DB 为准（`AgentRoleDefinition.IsBuiltIn`），`AgentType` 降为镜像 + parity 测试；前端按 flag 分区，删硬编码列表（F19 方案） |
| 34 | 设计文档「已锁定」决策被用户实战推翻（如 F13 S3 单条→列表） | 把 feature-dev 高风险闸口的「先设计」误解为「冻结需求」 | 设计文档决策节标注「待用户拍板 / 可修订」；用户反馈优先，直接改文档 + 改实现（见 08 §8.12） |
| 35 | 用 `curl -d '{"name":"中文"}'` 调后端返回 400，但 ASCII 正常 | Git Bash 在 inline JSON 里把中文 UTF-8 编码成乱码，后端收到非法 JSON | 浏览器/前端 axios 正常；命令行验证中文用 `python -c "urllib.request(...json.dumps(body).encode('utf-8'), headers={'Content-Type':'application/json; charset=utf-8'})"` |

**教训：** 控制器层的输入校验若比 handler 内部兜底更严，会产生「handler 本可处理却被挡在门外」的静默失败；列表页用 OR 聚合错误会让单点 400 拖垮整页——错误隔离要逐请求处理。

---

## 复盘自测

- 编译报错、运行期炸、数据写不进、并发不准、环境不对，分别先查什么？（背出口诀）
- 架构违规为什么编译不报错？靠什么拦截？
- 为什么 DI 注册后要第一时间写个空测试验证解析成功？
- 多方案认证报 `no DefaultChallengeScheme` 怎么修？handler 不适用时该 `NoResult()` 还是 `Fail()`？
- 运行时 `no such table` 但模型里有该实体，通常是什么反模式？

---

## 参考代码

- `phases/phase-1-baseline-mvp.md` 的完整踩坑表（346 行）
- `phases/phase-5-security-hardening.md`（#27~#31 的完整背景）
- `docs/learning/10-phase5-security-learnings.md`（三个排障实录详解）
- `Directory.Build.props`（TreatWarningsAsErrors = true）
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
