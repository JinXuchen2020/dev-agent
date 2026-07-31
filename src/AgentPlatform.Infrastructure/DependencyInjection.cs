using System;
using AgentPlatform.Application.Abstractions;
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
using AgentPlatform.Infrastructure.Progress;
using AgentPlatform.Infrastructure.Sandbox;
using AgentPlatform.Infrastructure.Security;
using AgentPlatform.Infrastructure.Services;
using AgentPlatform.Infrastructure.Tools;
using AgentPlatform.Infrastructure.VectorStore;
using AgentPlatform.Infrastructure.Tokenizers;
using AgentPlatform.Infrastructure.Workflows;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
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
    /// <returns>The modified <see cref="IServiceCollection"/> so additional registrations can be chained.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
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
        // 是否配置了真实 LLM 端点：OpenAI / DeepSeek / VLLM 任一有值即视为已接入。
        var llmConfigured = !string.IsNullOrEmpty(configuration["OpenAI:Key"])
            || !string.IsNullOrEmpty(configuration["DeepSeek:Key"])
            || !string.IsNullOrEmpty(configuration["VLLM:Url"]);

        // 显式选择 Stub，或未配置任何真实 LLM 端点时，回退到 StubModelClient，
        // 保证无密钥的本地/开发/演示环境「发送消息」不会因模型未注册而 500。
        // 一旦配置任一真实端点，自动切换回 SemanticKernelModelClient，无需改 Provider。
        if (string.Equals(modelProvider, "Stub", StringComparison.Ordinal) || !llmConfigured)
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
        services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
        services.AddScoped<IAgentRoleDefinitionRepository, AgentRoleDefinitionRepository>();
        services.AddScoped<IAgentConfigurationRepository, AgentConfigurationRepository>();
        services.AddSingleton<IYamlConfigurationParser, YamlConfigurationParserService>();
        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();

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
        // 代码沙箱：默认 Process（进程级，真实可验证、不依赖 Docker）；显式配置 Sandbox:Provider=Docker 才走容器（需 Docker 运行环境）。
        var sandboxProvider = configuration.GetSection("Sandbox:Provider").Value;
        if (string.Equals(sandboxProvider, "Docker", StringComparison.Ordinal))
            services.AddScoped<ICodeSandbox, DockerCodeSandbox>();
        else
            services.AddScoped<ICodeSandbox, ProcessCodeSandbox>();

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
        }
        else
        {
            services.AddScoped<IShortTermMemory, InMemoryShortTermMemory>();
        }

        services.AddHttpContextAccessor();
        // 进程内缓存：用于租户凭据解析的短期缓存（仅缓存密文实体）；BYO 更新时显式失效。
        services.AddMemoryCache();
        services.AddScoped<ITenantProvider, TenantProvider>();
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
        services.AddScoped<IConditionEvaluator, JsConditionEvaluator>();

        // ── F20 S3 HITL 审批仓储 ──
        services.AddScoped<IHumanApprovalRepository, HumanApprovalRepository>();

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

        var elSection = configuration.GetSection("ExecutionLog");
        var executionLogSettings = new ExecutionLogSettings
        {
            RetentionDays = int.TryParse(elSection["RetentionDays"], out var retention) ? retention : 90,
            BatchWriteThreshold = int.TryParse(elSection["BatchWriteThreshold"], out var batch) ? batch : 50,
            SseEnabled = bool.TryParse(elSection["SseEnabled"], out var sse) && sse
        };
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(executionLogSettings));

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

        // AutoGenAgentOrchestrator + AutoGenSettings removed (Phase 3 cleanup): the [Obsolete]
        // orchestrator and its dead config block are gone; OrchestrationPrimitive is the only engine.

        services.AddScoped<ToolCallingDispatcher>();
        services.AddScoped<IToolExecutor, NativeToolExecutor>();
        services.AddScoped<IToolExecutor, SkillPackageExecutor>();
        services.AddScoped<IToolExecutor, McpClient>();

        // Register execution log cleanup background job
        services.AddHostedService<ExecutionLogCleanupJob>();

        // Register API key expiry monitoring background job
        services.AddHostedService<ApiKeyExpiryJob>();

        return services;
    }
}
