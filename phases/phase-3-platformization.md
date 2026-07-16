# 阶段三：平台化与模型优化（2–3 周）

> 学习目标：从"能跑"到"好用"——后端 API 化、前端可视化、监控可观测。
>
> 本文档合并自原 `phase-3-platformization.md`（阶段计划，已提交于 fbd1576）与原 `phase-3-checklist.md`（质量门禁清单，未提交）。两者分别为"计划"与"质量门禁"两个视角，现整合为单一 Phase 3 文档。

## 学习目标

- [x] **ASP.NET Core Web API**：Controller 模式、中间件管道、Swagger/Scalar UI 集成
- [x] **事件驱动的日志查询**：ExecutionLog 查询 API + SSE 进度推送
- [x] **React 前端架构**：Vite 项目初始化、Ant Design 组件、React Router 路由、zustand 状态管理
- [x] **React Flow**：节点 / 边 / 拖拽面板的自定义实现
- [x] **OpenTelemetry 实战**：Metrics / Traces / Logs 三信号、Prometheus + Grafana 集成（含蓝图 §8.2 `AppMetrics` Counter/Histogram 实现）
- [x] **CI/CD 入门**：GitHub Actions 配置、自动构建 + 测试
- [x] **性能基准测试**：k6 压力测试脚本、P50/P95/P99 理解、性能调优方法论

## 前置依赖

- [x] 阶段二已完成并提交
- [x] 阶段二的 BDD 验收全部通过
- [x] React + TypeScript 开发环境已就绪（Node.js >= 18）

## 任务清单

- [x] **后端服务**：ASP.NET Core Web API，启用 Swagger 接口文档（**知识点**：REST API 设计 + 文档化）
- [x] **Agent 配置模块**：YamlDotNet 解析配置 + EF Core 持久化 + 版本管理（**知识点**：配置管理 + 版本化）
- [x] **工作流编排模块**：基于自研状态机，**React Flow** 拖拽可视化配置（**知识点**：前端工作流编辑器）
- [x] **前端**：**React** (TypeScript + Vite + Ant Design)，通过 REST API 对接后端（**知识点**：全栈联调）
- [x] **监控**：OpenTelemetry 接入 Prometheus + Grafana（**知识点**：可观测性三支柱）
- [x] **自定义 AgentType 后端**：种子数据 + AgentRoles/Configurations CRUD API（**知识点**：多租户数据设计）
- [x] **前端角色面板**：预置 + 自定义角色分区展示，API 动态加载（**知识点**：前端动态数据加载）
- [x] **性能基准验证**：k6 脚本（单租户并发 5-20 工作流、步骤 P95 < 30s 阈值）
- [x] **ExecutionLog 查询 API**：实现 4 个端点（列表 / 步骤 / 详情 / 错误筛选）
- [x] **SSE 进度推送**：状态机执行时推送 `step_progress` 事件到前端
- [x] **前端进度面板**：步骤列表中实时展示当前步骤状态和进度条
- [x] **日志清理 Job**：定时删除 90 天前的日志

## 验收标准

- [x] 平台可以通过 Web 界面拖拽编排工作流
- [x] 前端预置和自定义角色分区展示，角色从 API 动态加载
- [x] OpenTelemetry 指标埋点覆盖 §8.1 定义的全部指标（api / workflow / model 三类）
- [x] Prometheus + Grafana 配置文件就绪
- [x] CI 自动构建 + 跑全量测试
- [x] k6 性能基准脚本就绪

---

## Phase 3 Scope（质量门禁范围）

- 后端 API 化：ASP.NET Core Web API + Swagger/Scalar UI
- 事件驱动的日志查询：ExecutionLog 查询 API + SSE 进度推送
- React 前端架构：Vite + Ant Design + React Router + zustand
- React Flow 拖拽工作流编辑器
- OpenTelemetry 监控（API/workflow/model 三类指标）
- Prometheus + Grafana 配置
- 自定义 AgentType（AgentRoles/AgentConfigurations CRUD）
- 前端角色面板（预置+自定义分区展示）
- ExecutionLog 4 端点查询 + SSE 进度推送
- 日志清理 Job（定时删除 90 天前日志）
- CI/CD GitHub Actions 自动构建+测试
- k6 性能基准测试脚本

## 1. Pre-flight Version Audit (Phase 3 Specific)

- [x] Scalar.AspNetCore version locked and recorded in blueprint
- [x] OpenTelemetry.Exporter.Prometheus.AspNetCore version locked
- [x] YamlDotNet version locked (Agent 配置模块)
- [x] React 19 + Ant Design 5 + zustand + React Router versions fixed
- [x] React Flow (reactflow) version locked
- [x] Prometheus + Grafana docker image versions fixed
- [x] k6 version documented
- [x] API signatures verified: OpenTelemetry Meter/Histogram/Counter API
- [x] API signatures verified: React Flow Node/Edge/Connection API
- [x] `dotnet build` passes with existing code before any new code added

## 2. BDD Scenarios (Phase 3 Specific)

- [x] `ExecutionLogQuery.feature` — list/detail/steps/error filter endpoints
- [x] `AgentRoleCustomization.feature` — create/update custom roles
- [x] `SseProgressPush.feature` — SSE streaming of step_progress events
- [x] `WorkflowVisualEditor.feature` — drag-drop workflow creation and execution
- [x] `LogCleanupJob.feature` — automatic cleanup of old logs

**Edge case scenarios to verify:**

- [@] SSE reconnection after network interruption
- [@] Large workflow (50+ steps) in React Flow editor
- [@] Concurrent SSE subscribers for same workflow
- [@] Log cleanup job boundary conditions (exactly 90 days, empty table)

## 3. DDD Layer Rules (Phase 3 Specific)

New interfaces this phase:

- [x] `IYamlConfigurationParser` — Application.Abstractions — impl: `YamlConfigurationParserService` in Infrastructure
- [x] `IExecutionProgressBroadcaster` — Application.Abstractions — impl: `ExecutionProgressBroadcaster` in Infrastructure
- [x] `IDatabaseInitializer` — Application.Abstractions — impl: `DatabaseInitializer` in Infrastructure
- [x] `IAgentRoleDefinitionRepository` — Domain.Repositories — impl: `AgentRoleDefinitionRepository` in Infrastructure
- [x] `IAgentConfigurationRepository` — Domain.Repositories — impl: `AgentConfigurationRepository` in Infrastructure
- [x] Domain project .csproj still has zero external NuGet dependencies

## 4. DI Registration (Phase 3 Specific)

- [x] `IYamlConfigurationParser` -> `YamlConfigurationParserService` — lifetime: Singleton
- [x] `IExecutionProgressBroadcaster` -> `ExecutionProgressBroadcaster` — lifetime: Singleton
- [x] `IDatabaseInitializer` -> `DatabaseInitializer` — lifetime: Scoped
- [x] `IAgentRoleDefinitionRepository` -> `AgentRoleDefinitionRepository` — lifetime: Scoped
- [x] `IAgentConfigurationRepository` -> `AgentConfigurationRepository` — lifetime: Scoped
- [x] `ExecutionLogCleanupJob` registered as HostedService
- [x] OpenTelemetry metrics registered via `AddMeter` for both Api and Workflow
- [x] All new MediatR command/query handlers auto-registered via assembly scan

## 5. Configuration-First (Phase 3 Specific)

- [x] `ExecutionLogSettings` — retention days, batch write threshold, SSE enabled
- [x] `StateMachineSettings` — max retry count, step timeout, rollback timeout
- [x] `AutoGenSettings` — max rounds, idle interval, retry attempts, default model
- [x] OpenTelemetry endpoint configuration (`/metrics`)
- [x] CORS `AllowedOrigins` configuration (zero-length array protection)
- [x] Prometheus scraping endpoint path configurable
- [x] No hardcoded model names, GUIDs, or magic numbers in business code
- [x] All configuration registered in `appsettings.json` AND `appsettings.QuickStart.json`

## 6. EF Core Mapping (Phase 3 Specific)

- [x] `AgentRoleDefinition` aggregate: new `AgentRoleDefinitions` DbSet
- [x] `AgentConfiguration` aggregate: new `AgentConfigurations` DbSet
- [x] `AgentRoleDefinitionConfiguration`: `IEntityTypeConfiguration<AgentRoleDefinition>`
- [x] `AgentConfigurationConfiguration`: `IEntityTypeConfiguration<AgentConfiguration>`
- [x] Seed data for 6 pre-defined agent roles
- [x] Seed data for default agent configurations
- [x] `dotnet ef migrations add` succeeds (Phase2MultiAgent exists, includes Phase 3 tables)
- [x] Migration does not break existing tables

## 7. Concurrency and Lifecycle (Phase 3 Specific)

- [x] `ExecutionProgressBroadcaster`: Singleton with `ConcurrentDictionary` for per-workflow channels
- [x] `ExecutionProgressBroadcaster`: channels cleaned up on subscriber disconnect
- [x] SSE endpoint handles `OperationCanceledException` for client disconnects
- [x] `ExecutionLogCleanupJob`: BackgroundService with 24h interval, safe timer disposal
- [x] OpenTelemetry metrics: Meter is Singleton, thread-safe by design
- [x] `YamlConfigurationParserService`: Singleton, stateless (no mutable state)

## 8. Cross-Cutting Infrastructure (Phase 3 Specific)

- [x] New controllers use MediatR (not direct Application service calls)
  - `AgentRolesController` — CRUD through MediatR commands/queries
  - `AgentConfigurationsController` — CRUD through MediatR commands/queries
  - `ExecutionLogsController` — 4 query endpoints through MediatR
  - `WorkflowsController` — List/Get/Run through MediatR
  - `WorkflowProgressController` — SSE streaming via broadcaster
- [x] All commands marked `ICommand<T>` (trigger SaveChanges)
- [x] All queries NOT marked `ICommand<T>` (skip SaveChanges)
- [x] All domain events have Chinese documentation in AppDbContext
- [x] Scalar/Swagger UI enabled in all environments (no environment restriction)
- [x] CORS configured with zero-length array protection
- [x] Health Checks endpoint at `/health`
- [x] Exception handling: `UseExceptionHandler` + `UseStatusCodePages`
- [x] ProblemDetails for structured error responses
- [x] CorrelationIdMiddleware for request tracing
- [x] MetricsMiddleware for API request metrics (api.requests.total, api.errors.total, api.request.duration_ms)
- [x] OpenTelemetry Prometheus exporter at `/metrics`
- [x] All async methods pass `CancellationToken`
- [x] All implementation classes marked `internal sealed`
- [x] dotnet build — 0 warnings, 0 errors
- [x] dotnet test — all passing (63/63)

---

## Incremental Gate Sequence (Phase 3)

```
Module 1: ExecutionLog 查询 API (4 endpoints)
  - [x] GetExecutionLogsQuery + Handler
  - [x] GetExecutionLogDetailQuery + Handler
  - [x] GetExecutionLogStepsQuery + Handler
  - [x] ExecutionLogsController wired through MediatR
  - [x] dotnet build 0 warnings
  - [x] dotnet test all green

Module 2: SSE 进度推送
  - [x] IExecutionProgressBroadcaster interface
  - [x] ExecutionProgressBroadcaster implementation (Channel-based)
  - [x] WorkflowProgressController SSE endpoint
  - [x] Progress events published from state machine
  - [x] dotnet build 0 warnings
  - [x] dotnet test all green
> 🔍 **强制**：合入前必须走 `ddd-code-reviewer`，核对阶段三验收标准「SSE 进度推送」+ 蓝图对应章节；重点验证 `ExecutionProgressBroadcaster` 订阅/取消订阅无内存泄漏（历史 P1 已暴露 Subscribe 无取消）。

Module 3: 日志清理 Job
  - [x] ExecutionLogCleanupJob (BackgroundService)
  - [x] Configurable retention days via ExecutionLogSettings
  - [x] 24h interval timer
  - [x] dotnet build 0 warnings
  - [x] dotnet test all green

Module 4: React 前端架构
  - [x] Vite + React 19 + TypeScript + Ant Design setup
  - [x] zustand state management
  - [x] React Router navigation (9 pages)
  - [x] axios-based REST API integration
  - [x] npm build succeeds

Module 5: React Flow 工作流编辑器
  - [x] Custom workflow editor page
  - [x] Drag-drop step nodes
  - [x] Edge connections between steps
  - [x] Save and execute workflow
  - [x] npm build succeeds
> 🔍 **强制**：合入前必须走 `ddd-code-reviewer`，核对阶段三验收标准「Web 界面拖拽编排工作流」；重点验证拖拽保存/执行是否真连通状态机、50+ 步大工作流不崩。

Module 6: OpenTelemetry 监控
  - [x] DiagnosticsConfig (Api layer metrics)
  - [x] WorkflowMetrics (Application layer metrics)
  - [x] MetricsMiddleware for request tracking
  - [x] Prometheus exporter endpoint
  - [x] prometheus.yml + grafana-dashboard.json + docker-compose.monitoring.yml
  - [x] dotnet build 0 warnings
  - [x] dotnet test all green
> 🔍 **强制**：合入前必须走 `ddd-code-reviewer`，核对蓝图 §8.1/§8.2 全部指标（api/workflow/model 三类）；重点验证指标真实埋点、非空转。

Module 7: 自定义 AgentType (AgentRoles CRUD)
  - [x] AgentRoleDefinition aggregate root
  - [x] AgentRoleDefinitionRepository + configuration
  - [x] AgentRolesController CRUD endpoints
  - [x] AgentConfiguration aggregate root
  - [x] AgentConfigurationRepository + configuration
  - [x] AgentConfigurationsController CRUD endpoints
  - [x] Seed data for 6 roles + 3 default configurations
  - [x] dotnet build 0 warnings
  - [x] dotnet test all green

Module 8: 前端角色面板
  - [x] Built-in vs custom roles partitioned display
  - [x] API dynamic loading
  - [x] npm build succeeds

Module 9: CI/CD + 性能基准
  - [x] GitHub Actions workflow (build + test + npm build)
  - [x] k6 performance benchmark script
  - [x] Staged ramp (5-20 concurrent VUs)
  - [x] Workflow step P95 < 30s threshold
```

## Final Regression

After all modules complete:

- [x] Full `dotnet build` — 0 warnings, 0 errors
- [x] Full `dotnet test` — all passing (63/63)
- [x] Architecture tests pass (6/6)
- [x] Application unit tests pass (13/13)
- [x] SpecFlow BDD tests pass (41/41)
- [x] Integration tests pass (3/3)
- [x] No P0/P1 audit findings
- [x] Phase document updated with retrospective
- [x] Blueprint Phase 3 checklist items marked complete

---

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。本 Phase 若新增/修订蓝图章节（如可视化编排、HITL 断点），须先对变更章节重跑该评审，再进 §0 的 `ddd-code-reviewer` 强制审查。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性蓝图能力"的模块（编排器 / 状态机 / 协作引擎 / 沙箱闭环 / SSE 广播 / 监控指标 / RAG / Tool Calling / 模型路由等——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图、依赖是否真实使用、注册接口方法是否非空壳 |
| 纯基础设施 / 结构卫生模块（仓储 / DI / EF 映射 / Redis / CRUD 控制器 / 配置 / CI） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名/接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 附录 C.6 / §8.2 / 阶段 X 验收标准"）。缺此项即视为未通过。

### Phase 3 强制范围（高风险叙事性模块）

- **`WorkflowStateMachineEngine`**（`RetryAsync` / `RollbackAsync` / `StartAsync`）：核对附录 C.6（回滚语义）/ C.7（可恢复性）；历史 P1 暴露 `RetryAsync`/`RollbackAsync` 为静默存根，已改为 `NotSupportedException`。
- **`ExecutionProgressBroadcaster`**：核对阶段三 SSE 验收标准；历史 P1 暴露 Subscribe 无取消订阅 → 内存泄漏，已补 `(Guid, ChannelReader)` + `Unsubscribe`。
- **Module 2 SSE 进度推送**：核对阶段三验收标准「SSE 进度推送」；重点验证订阅 / 取消订阅无内存泄漏。
- **Module 5 React Flow 工作流编辑器**：核对阶段三验收标准「Web 界面拖拽编排工作流」；重点验证拖拽保存 / 执行是否真连通状态机、50+ 步大工作流不崩。
- **Module 6 OpenTelemetry 监控**：核对蓝图 §8.1 / §8.2 全部指标（api / workflow / model 三类）；重点验证指标真实埋点、非空转。
- Module 7 自定义 AgentType（CRUD）属标准基础设施，走 `ddd-phase-quality-gate` 即可。

> 说明：本阶段两份审查记录互补——下方「DDD 对抗性代码审查修复记录」（已跑 `ddd-code-reviewer`）覆盖状态机 / 广播器 / 迁移；「Audit Findings」（跑 `ddd-phase-quality-gate`）覆盖控制器 / 接口结构卫生。Module 5/6 此前仅过 quality-gate，建议补一轮 reviewer。

### 蓝图对齐新增项（来自 2026-07-16 设计评审，待排期）

> 以下为蓝图附录 C 重写后（单一编排原语 + 统一 `WorkflowContext` + critic 循环 + 上下文伸缩）在平台化阶段须补做的工作。它们对应设计评审报告 `docs/blueprint-architecture-review-2026-07-16.md` 的 P2 项，不归 Phase 2 旧任务覆盖。

- [ ] **F3 上下文伸缩策略落地**：将 `WorkflowStateMachineEngine`（现 sequential 预设执行体）升级为消费统一 `WorkflowContext`；接入 Blackboard 共享工作区 + 逐步摘要压缩（`MaxContextTokens` 封顶）+ RAG 召回注入（见 C.3.1）。
- [ ] **F5 RAG 接地**：把知识检索接入 Agent 上下文——生成前经 `WorkflowContext.Retrieval` 注入召回物，终结 Phase 1 `PgVectorStore` 的"存根不接地"状态（核对蓝图 §一 RAG 章节 + C.3.1）。
- [ ] **F6 HITL 断点设计**：在 `negotiation` 预设中落地具体 HITL 断点——明确哪些步可暂停、人看到/能改什么、如何从 `HumanInterventionRequired` 事件恢复（配合 C.6 精准回滚目标）。
- [ ] **F8 质量闭环硬化**：区分"文档生成步"与"执行验证步"，标注哪些质量门是声明 vs 已证实（测试真实跑通，非空泛文档）。

> 🔍 上述四项合入前必须走 `ddd-code-reviewer`，核对对应蓝图章节（C.3.1 / §一 / C.6），报告须写明"已核对章节"。

## DDD 对抗性代码审查修复记录

审查时间: 2026-07-16 | 审查工具: ddd-code-reviewer

### 修复清单

| 严重级 | 文件 | 问题 | 修复 |
|--------|------|------|------|
| P0 | `Migrations/20260715030832_Phase3AgentConfiguration.cs` | 迁移 Up/Down 方法为空，AgentConfigurations 表不会通过迁移创建 | 写入了完整的 CreateTable 迁移内容 |
| P1 | `Domain/ValueObjects/ConfigurationVersion.cs` | 属性使用 `init`（公开），可通过初始化器绕过构造函数验证 | 改为 `private init` |
| P1 | `Workflows/WorkflowStateMachineEngine.cs` | `RetryAsync`/`RollbackAsync` 是存根方法，静默无操作 | 改为抛出 `NotSupportedException`，防止静默失败 |
| P1 | `Progress/ExecutionProgressBroadcaster.cs` + `Abstractions/IExecutionProgressBroadcaster.cs` | `Subscribe` 返回 `ChannelReader` 但订阅者 ID 未暴露导致无法取消订阅，内存泄漏 | `Subscribe` 返回 `(Guid, ChannelReader)`；新增 `Unsubscribe` 方法 |
| P2 | `Workflows/WorkflowStateMachineEngine.cs` | `StartAsync` 方法内缩进不一致（混合 8 空格/16 空格） | 统一为 12 空格方法体缩进 |
| P2 | `Workflows/WorkflowStateMachineEngine.cs` | 分支步骤类型 `"branch"` 硬编码字符串 | 提取为 `const string BranchStepType` |
| P2 | `Cache/InMemoryShortTermMemory.cs` | `GetAsync` 未检查 `CancellationToken` | 添加 `ct.ThrowIfCancellationRequested()` |
| P3 | `Api/Program.cs` | 过时注释 `// 阶段二启用: app.UseAuthorization();` | 已移除 |

### 构建与测试验证

- **dotnet build**: ✅ 0 warnings, 0 errors
- **dotnet test**: ✅ 63/63 passed (ArchitectureTests 6, Application.Tests 13, SpecFlowTests 41, IntegrationTests 3)
- **dotnet list package --vulnerable**: ✅ No vulnerable packages found

## Audit Findings (Phase-3 Quality Gate, 2026-07-16)

Issues found and fixed during Phase 3 quality gate audit (`ddd-phase-quality-gate`):

| Severity | Category | File | Finding | Fix Applied |
|----------|----------|------|---------|-------------|
| P1 | 12f (Controller actions) | `WorkflowProgressController.cs:41` | Subscribe returns tuple; `return BadRequest()` in async Task; `reader.ReadAllAsync` fails | Destructured tuple; replaced BadRequest with manual 400 response |
| P3 | 5 (Missing CancellationToken) | `IDatabaseInitializer.cs:11` | Interface method missing CancellationToken parameter | Added `CancellationToken ct = default` |
| P3 | 5 (Missing CancellationToken) | `DatabaseInitializer.cs` | Implementation and helper methods missing CancellationToken propagation | Added `ct` parameter and propagated through all EF Core calls |

### Waivers

None.

## Phase 3 High-Risk Predictions

Based on Phase 1-2 patterns, these are most likely to require multi-round fixes:

1. **SSE reconnection handling** — browser EventSource auto-reconnect may create duplicate subscribers. Consider subscriber dedup in ExecutionProgressBroadcaster.
2. **React Flow large workflows** — 50+ step workflows may cause rendering lag. Consider virtualization or pagination.
3. **Concurrent SSE subscribers** — multiple browsers watching same workflow. Current ConcurrentDictionary handles this but monitoring is needed.
4. **Log cleanup edge cases** — BackgroundService may overlap if cleanup takes >24h. Consider semaphore or lock.
5. **OpenTelemetry metric cardinality** — model call metrics tagged by provider+model may explode cardinality. Consider limiting unique tag combinations.

---

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-07-13
- **完成日期**：2026-07-15
- **完成度**：██████████ 100%

## 回顾（完成后填写）

### 做得好的

- Agent 配置模块完整实现了 YAML 解析 + EF Core 持久化 + 版本管理
- ExecutionLog 查询 API 和 SSE 进度推送机制已实现
- OpenTelemetry 指标埋点覆盖模型调用和工作流步骤

### 下次改进

- `RetryAsync`/`RollbackAsync` 当前为 NotSupportedException，需在后续实现真正的重试/回滚逻辑
- 分支步骤执行器未注册（仅 `*` 通配符执行器存在）
- `ListWorkflowsQuery` 在内存中过滤所有工作流，大数据量时需改为数据库端分页

### 对蓝图文档的反馈

- 蓝图 §8.2 提及 `AppMetrics` 但实际使用 OpenTelemetry API（无需额外包），建议更新文档
