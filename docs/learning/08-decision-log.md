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

## 8.7 认证：Policy Scheme vs 写死默认方案（Phase 5）

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-21，Phase 5 |
| **决策者** | 架构组 |

### 选项

| 方案 | 描述 | 问题 |
|------|------|------|
| **写死一个默认方案**（如恒 Bearer） | `AddAuthentication("Bearer")` | 带 `X-API-Key` 的请求走不到 ApiKey handler |
| **每个 `[Authorize]` 显式标注 `AuthenticationSchemes`** | 控制器级指定 | 侵入性强，容易漏标 |
| **Policy Scheme 动态分发** | `Smart` 方案按请求头 `ForwardDefaultSelector` 转发 | 无侵入，JWT/ApiKey 并存 |

### 选择：Policy Scheme（`Smart`）

**理由：**
- JWT（`Authorization` 头）和 API-Key（`X-API-Key` 头）需要**并存**，写死单一默认方案会让另一种失效。
- Policy scheme 集中在 `Program.cs` 一处分发，控制器无需逐个标注 `AuthenticationSchemes`。
- 无凭证时转发到 Bearer，返回标准 `WWW-Authenticate: Bearer`，401 语义正确。

### 后续影响
- ApiKey handler 必须遵守 `NoResult()`（不适用）vs `Fail()`（无效）语义，否则会短路其他方案。
- 修掉了 `No DefaultChallengeScheme found` 运行时异常（见 `06` #27、`10` §10.4）。

---

## 8.8 dev 登录端点：门控开关 vs 不提供（Phase 5）

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-21，Phase 5 |
| **决策者** | 架构组 |

### 选项

| 方案 | 问题 |
|------|------|
| **不提供**，手动用脚本签 token | 每次测试都要跑脚本，体验差 |
| **无条件提供 `/api/dev/login`** | 生产环境等于"任意发 token"漏洞 |
| **`DevLoginEnabled` 门控、默认 false** | 兼顾便利与安全 |

### 选择：门控开关，默认关闭

**理由：**
- 主 `appsettings.json` 设 `false`（安全默认），`appsettings.Development.json` 设 `true`（本地自动可用）。
- 端点仅当 `DevLoginEnabled=true` 才注册，生产零调试后门。
- 返回**裸 token**（Swagger bearer 弹窗会自动补 `Bearer ` 前缀，返回带前缀会变成 `Bearer Bearer xxx`）。

### 后续影响
- 同步给 Swagger + Scalar 补 `AddSecurityDefinition("Bearer")`，UI 出现 Authorize 按钮（见 `06` #29/#30）。

---

## 8.9 数据库初始化：MigrateAsync vs EnsureCreated（Phase 5）

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-21，Phase 5 |
| **决策者** | 架构组 |

### 选项

| 方案 | 问题 |
|------|------|
| **`EnsureCreatedAsync()`** | 只在 DB 不存在时一次性建表，与 EF 迁移混用会漏建后加的表 |
| **`MigrateAsync()`** | 按迁移历史增量建表，是有迁移项目的正确做法 |

### 选择：MigrateAsync（保留 EnsureCreated 仅作 InMemory 测试兜底）

**理由：**
- 项目已有 EF 迁移（Phase2/3/5），`EnsureCreated` 不读迁移历史，旧 DB 缺 `AgentConfigurations`/`ApiKeys`/`AuditLogs` → `no such table`。
- `MigrateAsync` 先 `GetPendingMigrationsAsync` 判空再迁移；catch 兜底 `EnsureCreated` 兼容 InMemory 测试。

### 后续影响
- 补落缺失迁移 `Phase5ApiKeyIndex`（`ApiKeys` 索引调整；否则 pending model change 抛异常）。
- 铁律：**改模型必须 `dotnet ef migrations add`**；本地 DB 漂移用删文件 + 迁移重建（见 `06` #31、`10` §10.4）。

---

## 8.11 codebase-optimizer 三维度质量门禁落地（2026-07-22）

| 属性 | 值 |
|------|-----|
| **时间** | 2026-07-22，Phase 5 过渡期 |
| **决策者** | 质量治理组 |

### 背景

Phase 5 安全加固完成后，需要补齐全库健康检查。已有两道门禁：
- `ddd-code-reviewer`（高风险模块代码审查）
- `ddd-phase-quality-gate`（DDD 结构卫生）

新增 `codebase-optimizer` 作为第三道门禁，覆盖 8 个维度：架构 → 代码质量 → 正确性 → 测试 → 性能 → 安全 → 工程化 → **桩代码替换进度** → **生产就绪度**。

### 选项

| 方案 | 描述 | 问题 |
|------|------|------|
| **只跑现有两个 skill** | 不引入新门禁 | 缺少全库健康扫描和桩代码替换进度检查 |
| **codebase-optimizer + 人工 review** | 自动化扫描 + 人工确认 | 效率低，人工 review 容易遗漏 |
| **codebase-optimizer 自动化模式** | 零确认，自主执行，每轮 commit+push | 风险高，但效率最优 |

### 选择：codebase-optimizer 自动化模式

**理由：**
- Phase 5 已积累大量技术债（上帝类、重复代码、缺测试），需要系统化扫描
- 自动化模式可一次完成 Round 1（阶段1：基础质量）+ Round 1（阶段2：进阶质量）两轮扫描
- 每个修复任务独立，可并行执行，回归验证通过后再提交

### 实施结果

**阶段1（基础质量）— 11 个问题全部修复：**

| 任务 | 维度 | 成果 |
|------|------|------|
| R1-T1+T7 | 🏗️架构 + 🐛正确性 | OrchestrationPrimitive 从636行拆分为门面(302行)，提取 SequentialOrchestrator + NegotiationOrchestrator；ConcurrentDictionary TTL驱逐已实现 |
| R1-T2+T3 | 🏗️架构 + 🐛正确性 | Program.cs从348行精简至94行，提取Auth/OpenApi/Infrastructure配置，新增JWT启动守卫 |
| R1-T4 | 🧪测试 | Infrastructure.Tests项目创建，17个测试通过 |
| R1-T5 | 🧹代码质量 | Truncate方法提取到StringHelpers.cs，三处调用统一 |
| R1-T6 | 🧹代码质量 | 10个Domain聚合根属性风格统一，ApiKey expiresAt未来校验 |
| R1-T8 | 🐛正确性 | Redis连接改为ConnectAsync+重试+超时配置 |
| R1-T9 | 🏗️架构 | Asp.Versioning.Mvc 8.1.0引入，7个Controller加[ApiVersion("1.0")] |
| R1-T10 | 🏗️架构 | Workflow项目确认无需创建 |
| R1-T11 | 🧪测试 | Api.Tests项目创建，9个端点契约测试通过 |

**阶段2（进阶质量）— 全维度扫描完成，0问题：**
- ⚡ 性能：N+1/同步阻塞/LINQ反模式/字符串拼接 — 无发现
- 🔒 安全：硬编码密钥/SQL注入/弱加密/敏感日志 — 无发现
- 📦 工程化：CI/CD完整，NuGet版本锁定良好，TreatWarningsAsErrors=true
- 📋 桩代码替换：2/2=100%（DomainEventBus已迁移，Workflow项目无需创建）
- 🚀 生产就绪度：API版本控制✅ / 启动守卫✅ / 优雅降级✅ / 秘密管理✅ / 健康检查✅ / 弹性模式✅

**构建与测试：0错误 0警告，143/143测试通过。**

### 踩坑记录

| # | 问题 | 原因 | 解决方案 |
|---|------|------|----------|
| 1 | `Asp.Versioning.Mvc 10.0.0` 安装失败 | 该版本仅支持 .NET 10，项目是 .NET 9 | 回退到 `Asp.Versioning.Mvc 8.1.0` + `Asp.Versioning.Mvc.ApiExplorer 8.1.0` |
| 2 | `SequentialOrchestrator.cs` 编译报错 CS1513 | 后台任务超时前创建了文件但未闭合 namespace 块 | 手动补上 namespace 的 `}` 闭合 |
| 3 | `SequentialOrchestrator.cs` 编译报错 CS1061 | 方法内 `using Microsoft.Extensions.DependencyInjection;` 重复声明导致 CS0105（TreatWarningsAsErrors） | 移除重复 using，保留文件顶部声明 |
| 4 | `RunSequentialAsync` 编译报错 CS1513 | 方法体缺少闭合 `}` | 在 foreach 循环后补上方法闭合 `}` |
| 5 | API 契约测试 8/9 失败 | 路由从 `/api/[controller]` 改为 `/api/v1/[controller]` 后，测试仍用旧路径 | 更新测试工厂为 `CreateAuthenticatedClient()`，修正 Health/Metrics 端点路径为 `/health` / `/metrics` |
| 6 | `dotnet test` 中文输出过滤失败 | PowerShell 对中文管道符处理不稳定 | 改用英文关键词 `Passed|Failed|Error|total` 或直接用 `Select-String -Pattern "!"` |
| 7 | `git push` 首次失败 | RPC curl 55 Recv failure（网络波动） | 重试 `git push origin codebase-optimizer/2026-07-22` 成功 |
| 8 | SQLite DB 临时文件被误加入暂存区 | `agent_platform.db-shm/.wal/.bak` 是运行时产物 | `git reset HEAD` 移除，不提交 |

### 后续影响

- `.quality-gate.json` 中 `codebaseOptimizer` 从 `not_run` 升级为 `PASSED`
- 分支 `codebase-optimizer/2026-07-22` 已推送 GitHub，可创建 PR
- Phase 6+ 的提交门禁会强制要求 `codebaseOptimizer` 包含 `PASSED`
- `codebase-optimizer/` 目录已纳入 git 跟踪，方便 reviewer 查看上下文

---

## 8.10 决策演化时间线

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

2026-07-21 (Phase 5 安全加固)
├── 认证：写死默认方案 → Policy Scheme（Smart，按请求头分发 JWT/ApiKey）
├── dev 登录：不提供/无条件 → DevLoginEnabled 门控（默认 false，返回裸 token）
├── DB 初始化：EnsureCreatedAsync → MigrateAsync（补落 Phase5ApiKeyIndex 迁移）
├── API Key：明文 → AES-256-GCM 加密 + DB 聚合 + 轮换/吊销/过期扫描
├── 多租户：TenantProvider 硬编码默认 → per-request 从 claim 解析
└── 加学习文档（第 10 篇 Phase 5 安全）

2026-07-22 (codebase-optimizer 三维度质量门禁落地)
├── 阶段1 Round 1：11 个问题全部修复（OrchestrationPrimitive 拆分、Program.cs 拆分、Infrastructure.Tests、API版本控制等）
├── 阶段2 Round 1：全维度扫描完成（性能/安全/工程化/桩代码替换进度/生产就绪度），0问题
├── 踩坑：Asp.Versioning.Mvc 10.0→8.1.0（.NET 9兼容）、SequentialOrchestrator 闭合括号、SQLite临时文件误暂存
├── .quality-gate.json codebaseOptimizer: not_run → PASSED
├── 分支 codebase-optimizer/2026-07-22 已推送 GitHub
└── 143/143 测试通过，0错误 0警告
```

---

## 复盘自测

- 模型路由为什么选 Flat Priority List 而不是模型特定降级链？
- Domain 事件为什么用适配器模式桥接 MediatR，而不是直接依赖？
- `ICommand<T>` 标记接口的选型，后续影响是什么？
- JWT + API-Key 并存为什么选 Policy Scheme 而不是写死默认方案？
- dev 登录端点为什么必须门控？DB 初始化为什么从 EnsureCreated 改成 MigrateAsync？

---

## 参考

- `AGENT_PLATFORM_BLUEPRINT.md` 修改日志（v1.0 ~ v1.5）
- `phases/phase-1-baseline-mvp.md` 的"已应用的重构"表（84 项）
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`
