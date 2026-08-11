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
| 工作流"假完成"：Status=Completed 但所有节点 Pending | 查 IsDag 等影响编排的标志是否持久化 | #36 编排标志未映射 |
| SaveChanges 报 DbUpdateConcurrencyException（本应 INSERT） | 查客户端 Guid 主键是否 ValueGeneratedNever | #37 客户端 Guid PK |
| BDD/集成测试无诊断输出、日志不落地 | 查测试宿主是否吞文件写 | #38 宿主吞文件写 |
| 改 bug 后测试仍"绿"但其实是旧 DLL | 查是否用了 --no-build | #39 --no-build 旧 DLL |
| 前端选了某模式/枚举但后端不生效 | 查枚举是 int 还是字符串收发 | #41 枚举须 int 收发 |
| git push 到 GitHub 失败 | 查执行环境出站网络 | #44 沙箱无出站网络 |
| 质量门 commit 被拒（缺 json/cleared 非 true） | 查 .quality-gate.json 是否同暂存 | #45 pre-commit 质量门 |
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

## 6.14 2026-08-11 新增坑（F12 / F8 收尾 + 一期闭环）

> 这批坑来自一期最后冲刺（F12 Tool/Code 全链路 e2e、F8 Negotiation 产品化）与仓库工程环境实测，是 #1~#35 之后补录的"收尾期"经验。

| # | 现象 | 根因 | 避免 |
|---|------|------|------|
| 36 | DAG 工作流 run 后整体 `Completed`，但所有 Code/Tool 节点 `State=Pending`、`Result` 空（"假完成"） | `Workflow._isDag` 未做 EF 映射，re-run 从 DB 重载 `IsDag` 复位 false，`SequentialOrchestrator.PrepareContext` 据此 fallback 到遗留 `wf.Steps` 投影（节点 Type=null+ConfigJson="{}"），真实 DAG `Nodes` 从未被编排 | 任何影响编排行为的布尔/标志字段都必须持久化；加 `WorkflowConfiguration` 映射 + 迁移 `PersistWorkflowIsDag`（含 `#pragma warning disable IDE0161`） |
| 37 | 新增含显式 `Guid.NewGuid()` 的子实体集合，`SaveChanges` 报 `DbUpdateConcurrencyException`（期望 INSERT 却发 UPDATE） | EF 对 client-generated Guid 主键默认 `ValueGeneratedOnAdd`，检测到已赋值就当"已存在"走 UPDATE | 客户端预置 Guid 的集合属性配置 `ValueGeneratedNever()` |
| 38 | Reqnroll/SpecFlow 步骤里 `File.WriteAllText`/`AppendAllText` 诊断日志"写了但文件不落地"，排查无输出 | 测试宿主进程（test host）对文件系统写受限/被重定向，`WriteAllText` 静默吞异常 | 诊断靠"断言失败抛 Exception 带 dump"或 `dotnet test > file.txt` 重定向取 stdout；不要在步骤里依赖落盘文件 |
| 39 | 改完 bug 跑 `dotnet test --no-build` 仍"绿"，但实际 DLL 还是旧的 | 此前 `dotnet build` 因 IDE0161（TreatWarningsAsErrors）失败，残留下次编译产物；`--no-build` 跳过重编直接用旧 DLL | 改 bug 后务必 `dotnet test`（不带 `--no-build`）强制重编；CI 里避免 `--no-build` 除非确定刚 build 过 |
| 40 | 断言"全节点 Completed"通过，但控制标记 Start/End 实际还是 Pending，误以为全成功 | Start(0)/End(1) 是控制标记节点，编排器不解析执行器，合法保持 Pending；仅可执行节点（Code=7/Tool=6）被标 Completed | 断言终态时排除控制标记，只校验 Code/Tool；或断言工作流级 `Status=Completed` |
| 41 | 前端选了"协商"模式，后端却按顺序跑 / 枚举对不上 | API 全局**未注册** `JsonStringEnumConverter`，`OrchestrationPreset` 以 **int** 收发（Negotiation=1, Sequential=0）；前端若用字符串 JSON 会反序列化失败或落默认 | 模型一致性铁律：枚举参数一律 int 收发，绝不可改字符串；约定写进类型注释与 BDD 步骤 |
| 42 | CI PR 工作流不触发 / 分支规则报"无匹配" | 仓库 CI 分支规则只认 `[master]`，不存在 `main`/`develop` 分支名 | 新分支/PR 基于 `master`；改 PR 分支工作流须先合 master |
| 43 | 新建源码目录被 `.gitignore` 静默忽略、构建产物缺文件 | `.gitignore` 含 `[Dd]ebug/` 通配，曾误伤名为 `Debug` 的源码目录（F25） | 建新目录前先排查重名；避免目录名命中常见忽略通配 |
| 44 | `git push` 到 GitHub 失败（沙箱无出站网络） | 当前执行环境无 GitHub 出站网络 | 只本地 `git commit`；CI（`ubuntu-latest`）自带 Docker，无 daemon 断言用 `[SkippableFact]`+`Skip.If` |
| 45 | pre-commit 质量门拒绝 commit，报"缺少 codebaseOptimizer 字段 / cleared 非 true" | 改 `src/` 须同暂存 `.quality-gate.json` 且 `cleared:true` 且含 `codebaseOptimizer`；`git commit --amend` 若 json 无实质差量会被判"未更新"而拒 | 对 json 做实质补充（如改 hash/notes）再 amend；新 feature 务必同笔暂存 `.quality-gate.json` |
| 46 | playwright-bdd 跑出 "testDir not found" / 步骤未定义 | playwright-bdd 9.x 的 `testDir` 须 `= defineBddConfig()` 生成目录；`workers:1`+`fullyParallel:false` 才稳 | 严格按 9.x 配置；逻辑变量（PORT 等）置 `defineConfig` 外 |
| 47 | `qa.mjs` lint 报 peer-deps 冲突安装失败 | 前端依赖版本错配，需 `--legacy-peer-deps` | 跑 lint/install 统一加 `--legacy-peer-deps` |

**收尾期最值钱的三课**：① 编排行为相关的布尔/标志字段（如 `IsDag`）必须落库，否则"重跑即复位"会制造 `Completed` 假完成的静默故障（#36）；② 测试诊断别信落盘文件、改 bug 后别用 `--no-build`（#38/#39）；③ 前端与 API 的枚举/模式必须对齐序列化方式（int vs string），这是 F8 最易踩的模型一致性坑（#41）。

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
