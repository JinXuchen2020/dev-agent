using System;
using System.Runtime.InteropServices;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.EventHandlers;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Cache;
using AgentPlatform.Infrastructure.Configuration;
using AgentPlatform.Infrastructure.Jobs;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using AgentPlatform.Infrastructure.Scheduling;
using AgentPlatform.Infrastructure.Progress;
using AgentPlatform.Infrastructure.Sandbox;
using AgentPlatform.Infrastructure.Security;
using AgentPlatform.Infrastructure.Services;
using AgentPlatform.Infrastructure.Tools;
using AgentPlatform.Infrastructure.Artifacts;
using AgentPlatform.Infrastructure.VectorStore;
using AgentPlatform.Infrastructure.Tokenizers;
using AgentPlatform.Infrastructure.Workflows;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using StackExchange.Redis;
#if USE_POSTGRESQL
using Npgsql.EntityFrameworkCore.PostgreSQL;
#endif

namespace AgentPlatform.Infrastructure;

/// <summary>
/// Provides extension methods for registering all Infrastructure layer services into the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core, model clients, repositories, caching, vector store, sandbox, and workflow services into the service collection.
    /// </summary>
    /// <param name="services">The service collection to add infrastructure services to.</param>
    /// <param name="configuration">The application configuration used to resolve connection strings and provider settings.</param>
    /// <param name="environment">The hosting environment, used to isolate stub model clients to test environments only.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> so additional registrations can be chained.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // 支持可插拔数据库架构 - 使用条件编译
        var dbType = configuration["Database:Type"] ?? "sqlite";
        var connectionString = dbType.ToLowerInvariant() == "sqlite"
            ? configuration.GetConnectionString("DefaultConnection")
            : configuration.GetConnectionString("PostgreSQL");

        services.AddDbContext<AppDbContext>(options =>
        {
#if USE_POSTGRESQL
            if (dbType.ToLowerInvariant() == "postgresql")
            {
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
#else
            // 默认使用 SQLite
            options.UseSqlite(connectionString);
#endif
        });

        var modelProvider = configuration.GetSection("ModelClient:Provider").Value;
        // 是否配置了真实 LLM 端点：OpenAI Key 或 BaseUrl（DeepSeek/vLLM 均兼容 OpenAI 协议，统一走 OpenAI 配置）。
        var llmConfigured = !string.IsNullOrEmpty(configuration["OpenAI:Key"])
            || !string.IsNullOrEmpty(configuration["OpenAI:BaseUrl"]);

        // 仅 Test 环境显式配置 Provider=Stub 时注册 StubModelClient。
        // Integration / Development / Production / Staging 均走真实 SemanticKernelModelClient；
        // 启动时已在 Program.cs 强制校验至少一个真实 Key（Test 环境豁免）。
        var isTestEnv = environment.IsEnvironment("Test");
        if (string.Equals(modelProvider, "Stub", StringComparison.Ordinal) && isTestEnv)
        {
            var stubResponse = configuration.GetSection("ModelClient:StubResponse").Value
                ?? "这是模拟回复，平台已正常运行。";
            services.AddScoped<IModelClient>(_ => new StubModelClient(stubResponse));
        }
        else
        {
            services.AddScoped<SemanticKernelModelClient>();
            services.AddScoped<IModelClient>(sp =>
                new ModelTelemetryDecorator(
                    sp.GetRequiredService<SemanticKernelModelClient>(),
                    sp.GetRequiredService<ILogger<ModelTelemetryDecorator>>()));
        }

        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IWorkflowVersionRepository, WorkflowVersionRepository>();
        // ── F23 模板市场（平台级，非租户隔离）仓储 ──
        services.AddScoped<AgentPlatform.Domain.Repositories.IWorkflowTemplateRepository,
            AgentPlatform.Infrastructure.Persistence.Repositories.WorkflowTemplateRepository>();
        services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
        // ── F22 已发布工作流（API / MCP 暴露）仓储 ──
        services.AddScoped<AgentPlatform.Domain.Repositories.IPublishedWorkflowRepository,
            AgentPlatform.Infrastructure.Persistence.Repositories.PublishedWorkflowRepository>();
        services.AddScoped<IAgentRoleDefinitionRepository, AgentRoleDefinitionRepository>();
        services.AddScoped<IAgentConfigurationRepository, AgentConfigurationRepository>();
        // ── F24 评估数据集（租户隔离）仓储 ──
        services.AddScoped<IEvaluationDatasetRepository, EvaluationDatasetRepository>();
        // ── F25 工作流调试会话（租户隔离）仓储 ──
        services.AddScoped<IDebugSessionRepository, DebugSessionRepository>();
        // ── F30 执行持久化：RunningExecution 仓储（耐久调度与崩溃恢复）──
        services.AddScoped<IRunningExecutionRepository, RunningExecutionRepository>();
        // ── F32 Agent 消息总线：durable 消息日志仓储 + 进程内总线（SCOPED=每次运行实例隔离）──
        services.AddScoped<IAgentMessageLogRepository, AgentMessageLogRepository>();
        services.AddScoped<AgentPlatform.Application.Abstractions.IAgentMessageBus,
            AgentPlatform.Infrastructure.Messaging.InProcessAgentMessageBus>();
        // ── F33 语义记忆：episodic 写回与语义召回（复用 IVectorStore 租户隔离）──
        services.Configure<AgentPlatform.Application.Abstractions.SemanticMemorySettings>(
            configuration.GetSection("SemanticMemory"));
        services.AddScoped<AgentPlatform.Application.Abstractions.ISemanticMemoryService,
            AgentPlatform.Infrastructure.Memory.SemanticMemoryService>();
        services.AddSingleton<IYamlConfigurationParser, YamlConfigurationParserService>();
        // 单例工具注册表：启动时把平台内置 workspace 工具（Codex 式自主编码能力）注册进去，
        // 供 AgenticOrchestrator 按 agent 的 AllowedToolNames 白名单动态选用（F29）。
        services.AddSingleton<IToolRegistry>(_ =>
        {
            var registry = new InMemoryToolRegistry();
            WorkspaceToolDefinitions.Seed(registry);
            return registry;
        });

        // Register Semantic Kernel text embedding service used by PgVectorStore.
        // Uses the same OpenAI key as the chat completion service.
#pragma warning disable SKEXP0001 // ITextEmbeddingGenerationService is experimental in SK 1.x
        services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var apiKey = configuration["OpenAI:Key"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "OpenAI:Key is required for text embedding generation in PgVectorStore.");
            }

#pragma warning disable SKEXP0010 // AddOpenAITextEmbeddingGeneration is experimental in SK 1.x
            var kernel = Kernel.CreateBuilder()
                .AddOpenAITextEmbeddingGeneration("text-embedding-3-small", apiKey)
                .Build();
#pragma warning restore SKEXP0010

            return kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        });
#pragma warning restore SKEXP0001

        // 向量存储：按部署配置由 VectorStoreFactory 选择实现。
        // postgresql + OpenAI Key 时使用 PgVectorStore；否则回退 InMemoryVectorStore（默认 SQLite 部署不崩）。
        services.AddScoped<PgVectorStore>();
        // InMemory 回退必须注册为 Singleton：入库的向量需在进程内跨请求保留，
        // 否则默认 SQLite 部署下每次请求都是空存储，RAG 检索退化为静默 no-op（R3 失效）。
        services.AddSingleton<InMemoryVectorStore>();
        services.AddScoped<IVectorStoreFactory, VectorStoreFactory>();
        services.AddScoped<IVectorStore>(sp => sp.GetRequiredService<IVectorStoreFactory>().Create());

        // RAG 配置与文档切分器
        services.Configure<AgentPlatform.Application.Abstractions.RagSettings>(
            configuration.GetSection("Rag"));
        services.Configure<AgentPlatform.Application.Abstractions.SandboxSettings>(
            configuration.GetSection("Sandbox"));
        services.Configure<AgentPlatform.Application.Abstractions.SearchSettings>(
            configuration.GetSection("Search"));
        services.AddHttpClient();
        // 工作流 HTTP 节点执行器使用的具名客户端：超时放宽到 35s（出站请求另有 30s 硬上限 CTS 保护）。
        services.AddHttpClient("workflow-http", client => client.Timeout = TimeSpan.FromSeconds(35));
        services.AddScoped<AgentPlatform.Application.Abstractions.IDocumentChunker,
            AgentPlatform.Infrastructure.Services.WordWindowChunker>();

        // 文档文本提取器：顺序敏感 —— Html 必须在 Plain 之前（text/html 两者都匹配，
        // 优先走标签剥离而非原文读出）。
        services.AddScoped<AgentPlatform.Application.Abstractions.IDocumentTextExtractor,
            AgentPlatform.Infrastructure.Services.PdfTextExtractor>();
        services.AddScoped<AgentPlatform.Application.Abstractions.IDocumentTextExtractor,
            AgentPlatform.Infrastructure.Services.HtmlTextExtractor>();
        services.AddScoped<AgentPlatform.Application.Abstractions.IDocumentTextExtractor,
            AgentPlatform.Infrastructure.Services.PlainTextExtractor>();
        services.AddScoped<AgentPlatform.Domain.Repositories.IKnowledgeBaseRepository,
            AgentPlatform.Infrastructure.Persistence.Repositories.KnowledgeBaseRepository>();
            // 代码沙箱：ProcessCodeSandbox 为唯一 ICodeSandbox 入口；隔离策略全交给 ISandboxIsolation。
            // 默认 Sandbox:Provider=Docker（优先容器强隔离，守护进程不可用时自动降级进程级隔离，fail-safe）；
            // 显式配置 Process 则仅走进程级隔离。
            services.AddScoped<ICodeSandbox, ProcessCodeSandbox>();

            // Docker 守护进程可用性探测（单例，构造时一次 ping，结果缓存）。
            services.AddSingleton<IDockerProbe, DockerProbe>();
            // DockerCodeSandbox 作为内部容器执行器，由 DockerSandboxIsolation 复用（不暴露为 ICodeSandbox）。
            services.AddScoped<DockerCodeSandbox>();

            // OS 级沙箱隔离：按 Sandbox:Provider + Docker 可用性 + 平台 + OsIsolation 解析（均 fail-safe，绝不阻断执行）。
            // Provider=Docker 且守护进程可用 → DockerSandboxIsolation（强隔离，复用 DockerCodeSandbox）；
            // 否则：Windows + AppContainer/Full → AppContainer（真实禁网 + JobObject 资源限额）；
            //       Windows + JobObject（默认）→ JobObject 资源限额；非 Windows / Off → Null（仅环境标记缓解项）。
            services.AddScoped<ISandboxIsolation>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<SandboxSettings>>().Value;
                var lf = sp.GetRequiredService<ILoggerFactory>();
                var probe = sp.GetRequiredService<IDockerProbe>();
                if (string.Equals(opts.Provider, "Docker", StringComparison.Ordinal) && probe.IsAvailable)
                    return new DockerSandboxIsolation(
                        lf.CreateLogger<DockerSandboxIsolation>(), probe, sp.GetRequiredService<DockerCodeSandbox>());
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || opts.OsIsolation == OsIsolationMode.Off)
                    return new NullSandboxIsolation(lf.CreateLogger<NullSandboxIsolation>());
                if (opts.OsIsolation == OsIsolationMode.AppContainer || opts.OsIsolation == OsIsolationMode.Full)
                    return new AppContainerSandboxIsolation(lf.CreateLogger<AppContainerSandboxIsolation>(), Options.Create(opts));
                return new JobObjectSandboxIsolation(lf.CreateLogger<JobObjectSandboxIsolation>(), Options.Create(opts));
            });

        // 搜索提供方：真实 HTTP（SerpApi），密钥走 SearchSettings / 环境变量，不落库。
        // Provider 决定具体实现（当前仅 SerpApi；其余值启动即报错，避免静默失败）。
        var searchProviderName = configuration.GetSection("Search:Provider").Value ?? "SerpApi";
        if (string.Equals(searchProviderName, "SerpApi", StringComparison.Ordinal))
            services.AddScoped<AgentPlatform.Application.Abstractions.ISearchProvider,
                AgentPlatform.Infrastructure.Search.SerpApiSearchProvider>();
        else
            throw new InvalidOperationException($"不支持的搜索提供方：{searchProviderName}");

        // ── F13 多租户凭据配置（模型 + 搜索 BYO-Key / 平台内置）──
        // 凭据设置仓储（EF Core，租户隔离由 AppDbContext.HasQueryFilter 强制）。
        services.AddScoped<AgentPlatform.Domain.Repositories.ITenantCredentialSettingRepository,
            AgentPlatform.Infrastructure.Persistence.Repositories.TenantCredentialSettingRepository>();
        // 租户凭据解析（含短期内存缓存，仅缓存密文实体）。
        services.AddScoped<AgentPlatform.Application.Abstractions.ITenantCredentialResolver,
            AgentPlatform.Infrastructure.Credentials.TenantCredentialResolver>();
        // 租户 BYO 模型客户端解析（解密后构建 SemanticKernelModelClient，核心隔离点）。
        services.AddScoped<AgentPlatform.Application.Abstractions.ITenantModelClientResolver,
            AgentPlatform.Infrastructure.Models.TenantModelClientResolver>();
        // 平台模型目录（运营方配置的 RouterSettings.Candidates）。
        services.AddScoped<AgentPlatform.Application.Abstractions.IPlatformModelProvider,
            AgentPlatform.Infrastructure.Credentials.PlatformModelsProvider>();
        // F14 供应商模型发现（填 Key+BaseUrl 后拉取可访问模型清单，OpenAI 兼容 GET /models）。
        services.AddScoped<AgentPlatform.Application.Abstractions.IProviderModelDiscovery,
            AgentPlatform.Infrastructure.Models.ProviderModelDiscovery>();

        var cacheProvider = configuration.GetSection("Cache:Provider").Value;
        if (string.Equals(cacheProvider, "Redis", StringComparison.Ordinal))
        {
            var redisSection = configuration.GetSection("Redis");
            var redisSettings = new RedisSettings
            {
                ConnectionString = redisSection["ConnectionString"] ?? "localhost:6379",
                DefaultExpirySeconds = int.TryParse(redisSection["DefaultExpirySeconds"], out var expiry) ? expiry : 3600,
                KeyPrefix = redisSection["KeyPrefix"] ?? "agent-platform:"
            };
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(redisSettings));
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("AgentPlatform.Infrastructure.RedisConnection");
                var redisConnection = configuration.GetConnectionString("Redis")
                    ?? configuration.GetSection("Redis:ConnectionString").Value
                    ?? "localhost:6379";

                // Retry up to 3 times with exponential backoff for transient failures
                var maxAttempts = 3;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var conn = ConnectionMultiplexer.Connect(
                            new ConfigurationOptions
                            {
                                EndPoints = { redisConnection },
                                ConnectTimeout = 5000,
                                SyncTimeout = 5000,
                                AbortOnConnectFail = false  // graceful degradation
                            });
                        logger.LogInformation("Redis connected successfully to {Endpoint}", redisConnection);
                        return conn;
                    }
                    catch (Exception ex) when (attempt < maxAttempts)
                    {
                        logger.LogWarning(ex,
                            "Redis connection attempt {Attempt}/{MaxAttempts} failed, retrying in {Delay}ms",
                            attempt, maxAttempts, attempt * 1000);
                        Thread.Sleep(attempt * 1000);
                    }
                }

                // Last attempt: throw so the caller can decide to fall back to InMemoryShortTermMemory
                logger.LogError("Redis connection failed after {MaxAttempts} attempts", maxAttempts);
                throw new InvalidOperationException(
                    $"Redis connection to '{redisConnection}' failed after {maxAttempts} attempts. " +
                    "Set Cache:Provider=Memory or ensure Redis is reachable.");
            });
            services.AddScoped<IShortTermMemory, RedisShortTermMemory>();
            // F21 多实例调度防重：Redis 分布式锁（Redis 不可用时内部降级放行）。
            services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
        }
        else
        {
            services.AddScoped<IShortTermMemory, InMemoryShortTermMemory>();
            // F21 本地 / 单实例 / 测试：进程内锁回退。
            services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();
        }

        services.AddHttpContextAccessor();
        // 进程内缓存：用于租户凭据解析的短期缓存（仅缓存密文实体）；BYO 更新时显式失效。
        services.AddMemoryCache();
        services.AddScoped<ITenantProvider, TenantProvider>();
        // 后台调度 / 匿名 Webhook 的 scope 内租户注入持有器（TenantProvider 优先读此覆盖值）。
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAesEncryptor, AesGcmEncryptor>();
        services.AddScoped<IPromptSanitizer, PromptSanitizer>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IApiKeyEncryptionService, ApiKeyEncryptionService>();
        services.AddScoped<IKeyRotationService, KeyRotationService>();
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.AddScoped<PromptInjectionService>();
        services.AddScoped<IResiliencePipelineProvider, ResiliencePipelineProvider>();
        // Token counter singleton — stateless, safe to share across all workflows.
        services.AddSingleton<ITokenCounter, TokenCounter>();

        // ── Orchestration Primitive (Blueprint C.2) ──
        // Single engine: OrchestrationPrimitive replaced the legacy WorkflowStateMachineEngine,
        // AutoGenAgentOrchestrator and StubWorkflowEngine.
        services.AddScoped<IOrchestrationPrimitive, OrchestrationPrimitive>();
        services.AddScoped<OrchestrationPrimitive>();

        // Obsolete - replaced by IOrchestrationPrimitive (Blueprint C.2).
        // Registered only to satisfy DI contract while awaiting removal in Phase 3.
        services.AddScoped<IStateMachineEngine>(sp =>
            throw new InvalidOperationException(
                "IStateMachineEngine is obsolete and has been replaced by IOrchestrationPrimitive. " +
                "Use OrchestrationPreset.Sequential or OrchestrationPreset.Negotiation instead."));

        // Step executors for the engine
        services.AddScoped<IStepExecutor, AgentCallStepExecutor>();
        services.AddScoped<IStepExecutor, CriticStepExecutor>();
        services.AddScoped<IStepExecutor, KnowledgeRetrievalStepExecutor>();
        services.AddScoped<IStepExecutor, ToolStepExecutor>();
        services.AddScoped<IStepExecutor, CodeStepExecutor>();

        // ── F20 节点全家桶（S1）执行器 + 条件求值器 ──
        services.AddScoped<IStepExecutor, HttpStepExecutor>();
        services.AddScoped<IStepExecutor, ConditionStepExecutor>();
        services.AddScoped<IStepExecutor, VariableStepExecutor>();
        services.AddScoped<IStepExecutor, DelayStepExecutor>();
        services.AddScoped<IStepExecutor, SubWorkflowStepExecutor>();
        services.AddScoped<IStepExecutor, UserInputStepExecutor>();
        // ── F29 Agentic Agent Primitive：自主控制循环节点 ──
        services.AddScoped<IStepExecutor, AgenticStepExecutor>();
        services.AddScoped<IConditionEvaluator, JsConditionEvaluator>();

        // ── F20 S3 HITL 审批仓储 ──
        services.AddScoped<IHumanApprovalRepository, HumanApprovalRepository>();

        // ── F21 工作流触发器 + Chat 绑定仓储 ──
        services.AddScoped<IWorkflowTriggerRepository, WorkflowTriggerRepository>();
        services.AddScoped<IConversationWorkflowBindingRepository, ConversationWorkflowBindingRepository>();
        // F21 cron 调度计算（Cronos + IANA 时区）。
        services.AddSingleton<IScheduleCalculator, CronCalculator>();

        // Single-node runner for DAG debugging (POST /{id}/nodes/{nodeId}/run)
        services.AddScoped<IWorkflowNodeRunner, WorkflowNodeRunner>();

        // Strategy implementations for presets
        services.AddScoped<ISelectionStrategy, RoleBasedSelectionStrategy>();
        services.AddScoped<ITerminationCondition, CriticConvergenceTermination>();

        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        var smSection = configuration.GetSection("StateMachine");
        var stateMachineSettings = new StateMachineSettings
        {
            MaxRetryAttempts = int.TryParse(smSection["MaxRetryAttempts"], out var maxRetry) ? maxRetry : 3,
            StepTimeoutSeconds = int.TryParse(smSection["StepTimeoutSeconds"], out var timeout) ? timeout : 120,
            RetryDelayMs = int.TryParse(smSection["RetryDelayMs"], out var delayMs) ? delayMs : 1000,
            DefaultModelId = smSection["DefaultModelId"] ?? "deepseek-chat",
            MaxSummaryTokens = int.TryParse(smSection["MaxSummaryTokens"], out var maxTokens) ? maxTokens : 8000,
            AllowCriticOverride = bool.TryParse(smSection["AllowCriticOverride"], out var allowOverride) && allowOverride
        };
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(stateMachineSettings));

        var deSection = configuration.GetSection("DurableExecution");
        var durableExecutionSettings = new DurableExecutionSettings
        {
            LeaseTtlMinutes = int.TryParse(deSection["LeaseTtlMinutes"], out var leaseTtl) ? leaseTtl : 5,
            CheckpointBatchSize = int.TryParse(deSection["CheckpointBatchSize"], out var batchSize) ? batchSize : 5,
            CheckpointMaxAgeSeconds = int.TryParse(deSection["CheckpointMaxAgeSeconds"], out var maxAge) ? maxAge : 30
        };
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(durableExecutionSettings));

        // ── F32 多 Agent 协作防护配置（风暴/活锁熔断参数）──
        services.Configure<AgentCollaborationSettings>(configuration.GetSection("AgentCollaboration"));

        var elSection = configuration.GetSection("ExecutionLog");
        var executionLogSettings = new ExecutionLogSettings
        {
            RetentionDays = int.TryParse(elSection["RetentionDays"], out var retention) ? retention : 90,
            BatchWriteThreshold = int.TryParse(elSection["BatchWriteThreshold"], out var batch) ? batch : 50,
            SseEnabled = bool.TryParse(elSection["SseEnabled"], out var sse) && sse
        };
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(executionLogSettings));

        // ── F24 评估运行上限配置（可经 "Evaluation" 配置节覆盖）──
        services.Configure<AgentPlatform.Application.Evaluation.EvaluationSettings>(
            configuration.GetSection("Evaluation"));

        // Register SSE progress broadcaster as singleton (manages per-workflow channels)
        services.AddSingleton<IExecutionProgressBroadcaster, ExecutionProgressBroadcaster>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IModelRouter, ModelRouter>();
        services.AddSingleton<ICostController, CostController>();
        services.AddScoped<IDomainEventBus, DomainEventBus>();

        // Register domain event handlers for execution logging
        services.AddScoped<INotificationHandler<DomainEventNotification<WorkflowStarted>>, WorkflowStartedEventHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<StepCompleted>>, StepCompletedEventHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<StepFailed>>, StepFailedEventHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WorkflowCompleted>>, WorkflowCompletedEventHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WorkflowRolledBack>>, WorkflowRolledBackEventHandler>();
        // ── F33 语义记忆 episodic 写回（成功经验 + 失败教训）──
        services.AddScoped<INotificationHandler<DomainEventNotification<WorkflowCompleted>>,
            AgentPlatform.Application.EventHandlers.SemanticMemoryWriteBackHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WorkflowRolledBack>>,
            AgentPlatform.Application.EventHandlers.SemanticMemoryWriteBackHandler>();

        // AutoGenAgentOrchestrator + AutoGenSettings removed (Phase 3 cleanup): the [Obsolete]
        // orchestrator and its dead config block are gone; OrchestrationPrimitive is the only engine.

        services.AddScoped<ToolCallingDispatcher>();
        // ── F29 Agentic Agent Primitive：ReAct 控制循环引擎 ──
        services.AddScoped<AgenticOrchestrator>();
        services.AddScoped<IToolExecutor, NativeToolExecutor>();
        services.AddScoped<IToolExecutor, SkillPackageExecutor>();
        services.AddScoped<IToolExecutor, McpClient>();
        // ── F29 Agentic Agent Primitive：workspace / FS 工具执行器（在沙箱内读写跑）──
        // WorkspaceToolExecutor 既作为工具执行器，又实现 IWorkspaceRootProvider；
        // 注册为具名 scoped 实例，三个接口共用同一实例，保证编排器读到本次 run 的真实临时工作区根目录。
        services.AddScoped<WorkspaceToolExecutor>();
        services.AddScoped<IToolExecutor>(sp => sp.GetRequiredService<WorkspaceToolExecutor>());
        services.AddScoped<IWorkspaceRootProvider>(sp => sp.GetRequiredService<WorkspaceToolExecutor>());
        // 产物快照：把 run 结束时的临时工作区持久化到 data/agent-runs/{runId}/，供平台预览/下载。
        services.AddScoped<IArtifactStore, ArtifactStore>();
        // 运行历史记录（落库 + 查询）。
        services.AddScoped<IAgentRunRecorder, AgentRunRecorder>();

        // Register execution log cleanup background job
        services.AddHostedService<ExecutionLogCleanupJob>();

        // Register API key expiry monitoring background job
        services.AddHostedService<ApiKeyExpiryJob>();

        // F21 定时触发器后台调度器（轮询到期 Schedule，分布式锁防重）。
        services.AddHostedService<WorkflowScheduler>();

        return services;
    }
}
