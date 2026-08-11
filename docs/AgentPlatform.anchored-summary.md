# Agent Platform Anchored Summary

## Goal
- Phase 1 MVP complete: 7 projects scaffolded, DDD clean architecture, MediatR pipeline, routing/cost-control, domain events, ITenantScoped entities, SpecFlow BDD acceptance, ConversationsController through MediatR.
- Phase 2 complete: Multi-agent workflow with state machine, Redis short-term memory, AutoGen orchestration, ExecutionLog persistence, swappable database, CQRS query endpoints.
- Phase 3 complete: 平台化 — 可视化编排 (React Flow 拖拽编辑器 + 9 页面全功能前端)、OpenTelemetry 监控 (API/workflow/model 三类指标, Prometheus + Grafana)、自定义 AgentType (AgentRoles/AgentConfigurations CRUD)、ExecutionLog 4 端点查询 + SSE 进度推送、日志清理 Job、CI/CD GitHub Actions、k6 性能基准脚本

## Constraints & Preferences
- DDD dependency direction: Api → Application → Domain, Infrastructure → Application, Workflow → Application, Domain zero external dependencies
- .NET 9, MediatR v12 (built-in DI), Semantic Kernel 1.30, Scalar.AspNetCore
- Domain uses `IDomainEvent` pure interface; Application bridges via `DomainEventBus` adapter to MediatR
- SpecFlow BDD tests in `src/AgentPlatform.SpecFlowTests` with xUnit runner
- Blueprint v1.5 + Phase 1 full optimization pass + Phase 2 delivered
- IOptions for: TenantSettings, ModelDefaults, RouterSettings, PricingSettings, AutoGenSettings, RedisSettings, StateMachineSettings, ExecutionLogSettings; JSON camelCase serialization, Scalar OpenAPI UI + Swagger UI 默认启用
- Polly 8.5 non-generic ResiliencePipeline wrapped via IResiliencePipelineProvider interface (Application层不暴露Polly类型)
- UnitOfWork: IUnitOfWork 接口 + MediatR pipeline behavior (先分发领域事件, 再SaveChangesAsync, 仅对 ICommand<T> 标记的命令)
- ProblemDetails 全局异常处理, CORS 可配 (零长度数组保护), HealthChecks /health 端点
- ITenantScoped 接口在 Domain.Abstractions, 所有聚合根实现; Global Query Filter 通过 ITenantProvider 启用 (单租户模式)
- DomainEventBus 位于 Infrastructure 层, 实现 IDomainEventBus 接口
- WorkflowStateMachineEngine: Scoped 生命周期, 重试计数器在内存, 分支执行器待注册

## Progress
### Done
- ✅ 6 projects scaffolded + 1 SpecFlowTests, DDD dependency directions configured
- ✅ All Phase 1 code quality fixes: UnitOfWorkBehavior 事件顺序修复、ConversationsController → MediatR Command/Handler、CostController 接口抽象 ICostController、ModelRouter → ICostController、硬编码凭据移除、Scalar 限制 Development、Conversation/Message UpdatedAt 修复、空守卫补全、using 清理、WorkflowStep.UpdatedAt 位置调整、SendMessageRequest 移到 Models/、CORS 零长度保护、字符串比较 Ordinal、GetCostReport 重复 currency 修复
- ✅ C fixes: IAggregateRoot + _domainEvents + UnitOfWorkBehavior flush (DDD event pattern fixed), validation guards on all aggregates, ModelTelemetryDecorator stream exception capture fixed, ResiliencePipelineProvider ct fix, AgentCreated timestamp init-only
- ✅ H fixes: internal sealed on ~23 impl classes, sealed on public service classes, RoutingPolicyDomainService static, all domain string params null-guarded, CostController._budgetLimit removed
- ✅ M fixes: IModelRouter moved to Abstractions, PgVectorStore async cleanup, InMemoryShortTermMemory rename, hardcoded ModelRouter fallback removed, WorkflowStep.StepName = null!
- ✅ `dotnet build` 0 warnings 0 errors, `dotnet test` 7/7 (2 SpecFlow + 5 unit)
- ✅ Phase 2 all 9 modules complete: AgentType migration, state machine engine, Redis short-term memory, AutoGen orchestration, ExecutionLog persistence, swappable database, CQRS query endpoints, custom AgentRole CRUD, end-to-end integration
- ✅ 5 new SpecFlow feature files (AgentTypeMigration, WorkflowStateMachine, MultiAgentPipeline, ExecutionLog, CustomAgentRole) + 41/41 tests passing
- ✅ 4 new IOptions config classes (AutoGenSettings, RedisSettings, StateMachineSettings, ExecutionLogSettings)
- ✅ Phase2MultiAgent EF Core migration (8 tables, non-destructive)
- ✅ Quality gate: P0=0, P1=0, P2=0, P3=0 — Gate PASS
- ✅ `dotnet build` 0 warnings 0 errors, `dotnet test` 63/63 (6 Architecture + 13 Application + 41 SpecFlow + 3 Integration)
- ✅ DomainEventBus moved to Infrastructure (P1 DDD fix)
- ✅ WorkflowStateMachineEngine.GetStatusAsync returns actual state (P2 logic fix)

### Done (Phase 3)
- ✅ ExecutionLog 查询 API — 4 endpoints: 列表/详情/步骤/错误筛选
- ✅ SSE 进度推送 — IExecutionProgressBroadcaster + Channel-based singleton + WorkflowProgressController SSE endpoint
- ✅ 日志清理 Job — ExecutionLogCleanupJob (BackgroundService, 24h 间隔, configurable retention)
- ✅ Workflow CQRS — ListWorkflowsQuery + GetWorkflowQuery + RunWorkflowCommand + WorkflowsController
- ✅ Frontend React 19 + Vite + Ant D + zustand + React Router + axios — 9 pages with real API calls
- ✅ React Flow drag-drop workflow editor — WorkflowEditorPage (add steps, connect edges, save & run)
- ✅ OpenTelemetry metrics — DiagnosticsConfig + MetricsMiddleware + WorkflowMetrics (Application layer) covering §8.1
- ✅ Prometheus exporter at /metrics — OpenTelemetry.Exporter.Prometheus.AspNetCore registered in Program.cs
- ✅ CI/CD — .github/workflows/build-and-test.yml (dotnet build + test, npm build)
- ✅ Performance benchmark — benchmark/workflow-load-test.js (k6, 5-20 concurrent VUs, staged ramp)
- ✅ AgentRolesPage partitioned display — built-in vs custom roles with separate Card sections
- ✅ Phase 3 blueprint (phases/phase-3-platformization.md) — all checkboxes marked done, 100%
- ✅ Metrics: api.requests.total, api.errors.total, api.request.duration_ms (Middleware); model.call.total, model.call.duration_ms (SemanticKernelModelClient); workflow.step.duration_ms, workflow.completed.total (event handlers)
- ✅ Deploy config: prometheus.yml + grafana-dashboard.json + docker-compose.monitoring.yml
- ✅ Build: 0 warnings, 0 errors — Tests: 63/63 passing

### In Progress
- (none)

### Blocked
- (none)

## Key Decisions
- Domain 零外部依赖: IDomainEvent 纯接口, ITenantScoped 定义在 Domain.Abstractions
- ModelRouter 扁平化: flat priority list from RouterSettings config; 通过 ICostController 接口引用而非具体类
- CostController: Singleton + ICostController 接口抽象 + 每日自动重置 + 配置化定价表
- ICommand<T> marker interface: 仅命令触发 SaveChanges, 查询跳过; UnitOfWorkBehavior 先发事件再提交
- ModelDefaults/RouterSettings/PricingSettings: 全部通过 IOptions 注入, appsettings.json 配置
- ProblemDetails + StatusCodePages: 结构化异常响应
- WorkflowEngine: StubWorkflowEngine 占位, phase 2 替换
- 测试: NSubstitute 模拟 IOptions<T>, 通过 .Value.Returns() 注入配置
- StubModelClient: 条件注册，通过 `ModelClient:Provider=Stub` 启用，不依赖真实 API
- Money 值对象: 完整运算符集 (+ / <= / >=), 不允许跨货币比较
- AgentsController/ConversationsController: 独立控制器，均通过 MediatR 与 Application 通信
- IAggregateRoot: Domain.Abstractions 接口，聚合根自持 _domainEvents，UnitOfWorkBehavior 自动刷新
- internal sealed: Infrastructure 实现类全部 internal sealed，Application 公共服务 public sealed
- InMemoryShortTermMemory: 原名 RedisShortTermMemory 改名为准确名称
- ConnectionStrings:PostgreSQL 无默认值 (必须配置, 开发使用 dotnet user-secrets 或环境变量)
- Scalar/OpenAPI/Swagger: 所有环境默认启用 (已移除环境限制)

## Next Steps
- Phase 5: 安全加固（launch-blocking）— ✅ 已完成（JWT/API-Key 认证、RBAC、真实多租户隔离、速率限制、审计日志、API Key AES-256-GCM 加密）
- Phase 6: 前沿特性 — ✅ 已完成（第一期 Tier 1 共 29 个史诗全部 done：F5 行动层 / F6 Research / F8 Negotiation+Critic / F9–F12 沙箱与 e2e / F13–F19 多租户·i18n·Dashboard / F20–F28 工作流平台化 / F27–F28 BDD 全量 / F34 沙箱双层隔离）
- 第二期（真 Agent Harness 升级）已解锁：F29 Durable Execution / F30 Agent 实体化 / F31 消息总线 / F32 语义记忆 / F33 评估门禁——见 features/backlog.md
- 蓝图同步: 版本 v1.5+, Phase 2–6 清单已勾选，第一期全部完成

## Critical Context
- .NET 9, SK 1.30 (IChatCompletionService in Microsoft.SemanticKernel.ChatCompletion, metadata keys: "Usage.InputTokens", "Usage.OutputTokens")
- MediatR v12: `AddOpenBehavior(typeof(UnitOfWorkBehavior<,>))` 约束 `where TRequest : ICommand<TResponse>`
- EF Core: OwnsMany + `.ValueGeneratedOnAdd()` + OwnsOne column disambiguation
- Polly 8.5: non-generic ResiliencePipeline; return values via closure capture
- Program.cs: 条件 HttpsRedirection (Development/QuickStart 跳过), Scalar/Swagger 默认启用, CORS from Cors:AllowedOrigins, HealthChecks at /health
- `dotnet run --launch-profile QuickStart` → SQLite + Stub model + full config (ModelDefaults/Router/Pricing/Cors)
- Domain.Entities 全部实现 ITenantScoped (TenantId), Global Query Filter 已通过 ITenantProvider 启用 (单租户模式)
- Phase-1 all known issues resolved, 22 code quality items fixed in the final optimization round

## Relevant Files
- `phases/phase-1-baseline-mvp.md`: 完整阶段一记录
- `src/AgentPlatform.Domain/Abstractions/ITenantScoped.cs`: 多租户标记接口
- `src/AgentPlatform.Application/Abstractions/ICommand.cs`: UnitOfWork 过滤标记
- `src/AgentPlatform.Application/Abstractions/ICostController.cs`: 成本控制接口
- `src/AgentPlatform.Application/Behaviors/UnitOfWorkBehavior.cs`: MediatR pipeline, 先事件后提交
- `src/AgentPlatform.Application/Routing/Services/CostController.cs`: Singleton + daily reset + config pricing
- `src/AgentPlatform.Application/Routing/Services/ModelRouter.cs`: config-driven candidates, logging, 通过 ICostController 接口引用
- `src/AgentPlatform.Application/Conversations/Commands/CreateConversation/`: 创建会话 Command + Handler
- `src/AgentPlatform.Application/Conversations/Commands/SendMessage/`: 发送消息 Command + Handler (含 RAG 上下文)
- `src/AgentPlatform.Api/Models/SendMessageRequest.cs`: 消息请求 DTO (从 Controller 移出)
- `src/AgentPlatform.Infrastructure/Models/SemanticKernelModelClient.cs`: TokenUsage from Metadata, ToChatHistory extracted
- `src/AgentPlatform.Infrastructure/Persistence/TenantProvider.cs`: 多租户提供者 (读取 DefaultTenantId)
- `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`: HasQueryFilter 通过 ITenantProvider 启用
- `src/AgentPlatform.Infrastructure/Models/RoutingMiddleware/ModelTelemetryDecorator.cs`: streaming telemetry
- `src/AgentPlatform.Api/Program.cs`: ProblemDetails, CORS, HealthChecks, conditional HttpsRedirection/Scalar(Dev only)
- `src/AgentPlatform.Api/appsettings.json`: full ModelDefaults/Router/Pricing/Cors/OpenAI config
- `src/AgentPlatform.Api/Controllers/AgentsController.cs`: 独立 Agent CRUD 控制器 (MediatR)
- `src/AgentPlatform.Domain/Abstractions/IAggregateRoot.cs`: 聚合根事件自持接口
- `src/AgentPlatform.Infrastructure/Cache/InMemoryShortTermMemory.cs`: 内存缓存实现
- `src/AgentPlatform.Application/Abstractions/IModelRouter.cs`: ModelRouter 接口
- `Directory.Build.props`: net9.0, Nullable, TreatWarningsAsErrors, AnalysisLevel=latest
- `.editorconfig`: file-scoped namespaces, 4-space indent, CRLF
