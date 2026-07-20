using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.EventHandlers;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Agents;
using AgentPlatform.Infrastructure.Cache;
using AgentPlatform.Infrastructure.Configuration;
using AgentPlatform.Infrastructure.Jobs;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using AgentPlatform.Infrastructure.Progress;
using AgentPlatform.Infrastructure.Sandbox;
using AgentPlatform.Infrastructure.Tools;
using AgentPlatform.Infrastructure.VectorStore;
using AgentPlatform.Infrastructure.Workflows;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        if (string.Equals(modelProvider, "Stub", StringComparison.Ordinal))
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
        services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
        services.AddScoped<IAgentRoleDefinitionRepository, AgentRoleDefinitionRepository>();
        services.AddScoped<IAgentConfigurationRepository, AgentConfigurationRepository>();
        services.AddSingleton<IYamlConfigurationParser, YamlConfigurationParserService>();
        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();
        services.AddScoped<IVectorStore, PgVectorStore>();
        services.AddScoped<ICodeSandbox, DockerCodeSandbox>();

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
                var redisConnection = configuration.GetConnectionString("Redis")
                    ?? configuration.GetSection("Redis:ConnectionString").Value
                    ?? "localhost:6379";
                return ConnectionMultiplexer.Connect(redisConnection);
            });
            services.AddScoped<IShortTermMemory, RedisShortTermMemory>();
        }
        else
        {
            services.AddScoped<IShortTermMemory, InMemoryShortTermMemory>();
        }

        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<IResiliencePipelineProvider, ResiliencePipelineProvider>();
        // ── Orchestration Primitive (Blueprint C.2) ──
        // Single engine replacing WorkflowStateMachineEngine + AutoGenAgentOrchestrator + IWorkflowEngine
        services.AddScoped<IOrchestrationPrimitive, OrchestrationPrimitive>();
        // Legacy: StubWorkflowEngine kept for Phase 1 backward compat only.
        // Fully replaced by OrchestrationPrimitive. Scheduled for removal in Phase 3 cleanup.
#pragma warning disable CS0618 // Type is legacy - kept for backward compat
        services.AddScoped<IWorkflowEngine, StubWorkflowEngine>();
#pragma warning restore CS0618

        // Obsolete - replaced by IOrchestrationPrimitive (Blueprint C.2).
        // Registered only to satisfy DI contract while awaiting removal in Phase 3.
        services.AddScoped<IStateMachineEngine>(sp =>
            throw new InvalidOperationException(
                "IStateMachineEngine is obsolete and has been replaced by IOrchestrationPrimitive. " +
                "Use OrchestrationPreset.Sequential or OrchestrationPreset.Negotiation instead."));

        // Step executors for the engine
        services.AddScoped<IStepExecutor, AgentCallStepExecutor>();
        services.AddScoped<IStepExecutor, CriticStepExecutor>();

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
            DefaultModelId = smSection["DefaultModelId"] ?? "deepseek-chat"
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

        var agSection = configuration.GetSection("AutoGen");
        var autoGenSettings = new AutoGenSettings
        {
            MaxRounds = int.TryParse(agSection["MaxRounds"], out var rounds) ? rounds : 20,
            MaxIdleIntervalSeconds = int.TryParse(agSection["MaxIdleIntervalSeconds"], out var idle) ? idle : 30,
            MaxRetryAttempts = int.TryParse(agSection["MaxRetryAttempts"], out var retry) ? retry : 3,
            DefaultModelId = agSection["DefaultModelId"] ?? "deepseek-chat"
        };
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(autoGenSettings));
        // IAgentOrchestrator fully replaced by IOrchestrationPrimitive (Blueprint C.2)
        // AutoGenSettings is dead configuration — kept only for the [Obsolete] AutoGenAgentOrchestrator.
        // When that class is removed in Phase 3, remove this registration block as well.

        services.AddScoped<ToolCallingDispatcher>();
        services.AddScoped<IToolExecutor, NativeToolExecutor>();
        services.AddScoped<IToolExecutor, SkillPackageExecutor>();
        services.AddScoped<IToolExecutor, McpClient>();

        // Register execution log cleanup background job
        services.AddHostedService<ExecutionLogCleanupJob>();

        return services;
    }
}
