using System.Linq.Expressions;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.Debug;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Aggregates.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// The EF Core <see cref="DbContext"/> for the platform, enforcing tenant-scoped query filters and acting as the unit of work.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    private readonly Guid _tenantId;

    /// <summary>
    /// 当前使用的数据库类型
    /// </summary>
    public static string CurrentDbType { get; private set; } = "sqlite";

    /// <summary>
    /// 数据库类型配置键
    /// </summary>
    public const string DbTypeKey = "Database:Type";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDbContext"/> class and resolves the current tenant identifier.
    /// </summary>
    /// <param name="options">The options used to configure the database context.</param>
    /// <param name="tenantProvider">The provider that supplies the current tenant identifier for query filtering.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantId = tenantProvider.GetTenantId();
    }

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted agent aggregates.
    /// </summary>
    public DbSet<Agent> Agents => Set<Agent>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted workflow aggregates.
    /// </summary>
    public DbSet<Workflow> Workflows => Set<Workflow>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted conversation aggregates.
    /// </summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted tool definitions.
    /// </summary>
    public DbSet<ToolDefinition> ToolDefinitions => Set<ToolDefinition>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted custom agent role definitions.
    /// </summary>
    public DbSet<Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition> AgentRoleDefinitions =>
        Set<Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted agent configuration definitions.
    /// </summary>
    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted audit log entries.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted API keys.
    /// </summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted users (tenant-scoped).
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted knowledge bases (tenant-scoped).
    /// </summary>
    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted tenant credential settings (tenant-scoped).
    /// </summary>
    public DbSet<TenantCredentialSetting> TenantCredentialSettings => Set<TenantCredentialSetting>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted workflow version snapshots.
    /// </summary>
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted human-approval records (HITL, tenant-scoped).
    /// </summary>
    public DbSet<HumanApproval> HumanApprovals => Set<HumanApproval>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted published-workflow records (F22, tenant-scoped).
    /// </summary>
    public DbSet<AgentPlatform.Domain.Aggregates.PublishedWorkflows.PublishedWorkflow> PublishedWorkflows =>
        Set<AgentPlatform.Domain.Aggregates.PublishedWorkflows.PublishedWorkflow>();
    
    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted workflow triggers (webhook / schedule, tenant-scoped).
    /// </summary>
    public DbSet<WorkflowTrigger> WorkflowTriggers => Set<WorkflowTrigger>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted evaluation datasets (F24, tenant-scoped).
    /// </summary>
    public DbSet<EvaluationDataset> EvaluationDatasets => Set<EvaluationDataset>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to persisted conversation-to-workflow bindings (Chat trigger, tenant-scoped).
    /// </summary>
    public DbSet<ConversationWorkflowBinding> ConversationWorkflowBindings => Set<ConversationWorkflowBinding>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to platform-level workflow templates (F23).
    /// Templates are intentionally NOT tenant-scoped — shared across all tenants, no query filter.
    /// </summary>
    public DbSet<AgentPlatform.Domain.Aggregates.WorkflowTemplates.WorkflowTemplate> WorkflowTemplates =>
        Set<AgentPlatform.Domain.Aggregates.WorkflowTemplates.WorkflowTemplate>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> providing access to workflow debug sessions (F25).
    /// </summary>
    public DbSet<DebugSession> DebugSessions => Set<DebugSession>();

    /// <summary>
    /// Returns all aggregate roots currently tracked by the change tracker, used for dispatching domain events on save.
    /// </summary>
    /// <returns>A read-only collection of tracked <see cref="IAggregateRoot"/> instances.</returns>
    public IReadOnlyCollection<IAggregateRoot> GetTrackedAggregates()
    {
        return ChangeTracker
            .Entries()
            .Select(e => e.Entity)
            .OfType<IAggregateRoot>()
            .ToList();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
            {
                var param = Expression.Parameter(entity.ClrType, "e");
                var prop = Expression.Property(param, "TenantId");
                // Must use field reference (not Expression.Constant) so EF Core evaluates
                // _tenantId per DbContext instance at query time, not once at model-build time.
                // Without this, all requests share the first request's tenant ID — P0 isolation break.
                var body = Expression.Equal(prop,
                    Expression.Field(Expression.Constant(this, typeof(AppDbContext)), "_tenantId"));
                modelBuilder.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(body, param));
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
