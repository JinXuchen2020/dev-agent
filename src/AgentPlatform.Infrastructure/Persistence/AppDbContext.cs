using System.Linq.Expressions;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
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
                var body = Expression.Equal(prop, Expression.Constant(_tenantId));
                modelBuilder.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(body, param));
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
