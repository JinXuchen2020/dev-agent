using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// <see cref="IWorkspaceProvisioner"/> 的 EF 实现（F35）。
/// 回填对 18 个 <see cref="IWorkspaceScoped"/> 聚合 + 3 个补列实体（AuditLog / ExecutionLog /
/// AgentRunRecord）显式枚举执行（EF9 无非泛型 Set(Type)，显式写法 provider 无关、可读可审查）。
/// </summary>
internal sealed class WorkspaceProvisioner(
    AppDbContext context,
    IWorkspaceDirectory directory,
    ILogger<WorkspaceProvisioner> logger) : IWorkspaceProvisioner
{
    /// <inheritdoc />
    public async Task<Guid> EnsureDefaultWorkspaceAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await context.Workspaces
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsDefault, ct);

        if (existing is null)
        {
            existing = new Workspace(Guid.NewGuid(), tenantId, "Default", description: null, isDefault: true);
            context.Workspaces.Add(existing);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Created default workspace {WorkspaceId} for tenant {TenantId}.", existing.Id, tenantId);
        }

        directory.RegisterDefault(tenantId, existing.Id);
        return existing.Id;
    }

    /// <inheritdoc />
    public async Task BackfillEmptyWorkspaceIdsAsync(CancellationToken ct = default)
    {
        var defaults = await context.Workspaces
            .IgnoreQueryFilters()
            .Where(w => w.IsDefault)
            .ToListAsync(ct);

        foreach (var workspace in defaults)
        {
            directory.RegisterDefault(workspace.TenantId, workspace.Id);
        }

        var totalBackfilled = 0;
        foreach (var workspace in defaults)
        {
            // 18 个工作空间隔离聚合（Domain 层 IWorkspaceScoped）。
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Agents.Agent>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Workflows.Workflow>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Workflows.WorkflowVersion>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Workflows.RunningExecution>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Conversations.Conversation>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Conversations.ConversationWorkflowBinding>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.ToolDefinitions.ToolDefinition>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.AgentConfigurations.AgentConfiguration>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.ApiKeys.ApiKey>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Users.User>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.KnowledgeBases.KnowledgeBase>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.TenantCredentials.TenantCredentialSetting>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.HumanApprovals.HumanApproval>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.PublishedWorkflows.PublishedWorkflow>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.WorkflowTriggers.WorkflowTrigger>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Evaluation.EvaluationDataset>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.Debug.DebugSession>(workspace, ct);
            totalBackfilled += await BackfillAsync<AgentPlatform.Domain.Aggregates.AgentMessages.AgentMessageLog>(workspace, ct);

            // F35 补列但不叠加 workspace 过滤的 3 个实体（数据完整性回填）。
            totalBackfilled += await BackfillExtraAsync<AgentPlatform.Domain.Aggregates.AuditLogs.AuditLog>(workspace, ct);
            totalBackfilled += await BackfillExtraAsync<AgentPlatform.Domain.Aggregates.ExecutionLogs.ExecutionLog>(workspace, ct);
            totalBackfilled += await BackfillExtraAsync<AgentPlatform.Domain.Aggregates.AgentRuns.AgentRunRecord>(workspace, ct);
        }

        if (totalBackfilled > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Backfilled {Count} entities with empty WorkspaceId to their tenant default workspaces.", totalBackfilled);
        }
    }

    private async Task<int> BackfillAsync<TEntity>(Workspace workspace, CancellationToken ct)
        where TEntity : class, Domain.Abstractions.ITenantScoped, IWorkspaceScoped
    {
        var stale = await context.Set<TEntity>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == workspace.TenantId && e.WorkspaceId == Guid.Empty)
            .ToListAsync(ct);

        foreach (var entity in stale)
        {
            context.Entry(entity).Property(nameof(IWorkspaceScoped.WorkspaceId)).CurrentValue = workspace.Id;
        }

        return stale.Count;
    }

    private async Task<int> BackfillExtraAsync<TEntity>(Workspace workspace, CancellationToken ct)
        where TEntity : class
    {
        var stale = await context.Set<TEntity>()
            .IgnoreQueryFilters()
            .Where(e => EF.Property<Guid>(e, "TenantId") == workspace.TenantId
                        && EF.Property<Guid>(e, "WorkspaceId") == Guid.Empty)
            .ToListAsync(ct);

        foreach (var entity in stale)
        {
            context.Entry(entity).Property("WorkspaceId").CurrentValue = workspace.Id;
        }

        return stale.Count;
    }
}
