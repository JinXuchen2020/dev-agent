using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Aggregates.AgentMessages;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.Debug;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workspaces;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IWorkspaceRepository"/> 的 EF 实现（F35）。租户隔离由 AppDbContext 全局过滤器保证；
/// <see cref="CountBusinessEntitiesAsync"/> 显式枚举全部 18 个 <see cref="IWorkspaceScoped"/> 聚合
/// 在工作空间内的存量行数（删除守卫用；EF9 无非泛型 Set(Type)，显式写法可读可审查）。
/// </summary>
internal sealed class WorkspaceRepository(AppDbContext context) : IWorkspaceRepository
{
    public Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<Workspace?> GetDefaultAsync(CancellationToken ct = default) =>
        context.Workspaces.FirstOrDefaultAsync(w => w.IsDefault, ct);

    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken ct = default) =>
        await context.Workspaces.OrderBy(w => w.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Workspace>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await context.Workspaces.Where(w => ids.Contains(w.Id)).OrderBy(w => w.Name).ToListAsync(ct);

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) =>
        context.Workspaces.AnyAsync(w => w.Name == name.Trim(), ct);

    public async Task AddAsync(Workspace workspace, CancellationToken ct = default) =>
        await context.Workspaces.AddAsync(workspace, ct);

    public void Remove(Workspace workspace) => context.Workspaces.Remove(workspace);

    public async Task<int> CountBusinessEntitiesAsync(Guid workspaceId, CancellationToken ct = default)
    {
        // 删除守卫可能统计「非当前上下文工作空间」（Admin 在 W1 删 W2）——若沿用当前
        // workspace 过滤器，跨工作空间计数恒为 0。WorkspaceId 为全局唯一 Guid，按 id 计数
        // 不存在跨租户泄漏，故此处 IgnoreQueryFilters 后按 id 精确统计。
        var total = 0;
        total += await context.Set<Agent>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<Workflow>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<WorkflowVersion>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<RunningExecution>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<Conversation>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<ConversationWorkflowBinding>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<ToolDefinition>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<AgentConfiguration>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<ApiKey>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<User>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<KnowledgeBase>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<TenantCredentialSetting>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<HumanApproval>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<PublishedWorkflow>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<WorkflowTrigger>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<EvaluationDataset>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<DebugSession>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        total += await context.Set<AgentMessageLog>().IgnoreQueryFilters().CountAsync(e => e.WorkspaceId == workspaceId, ct);
        return total;
    }
}
