using AgentPlatform.Domain.Aggregates.Workspaces;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IWorkspaceMemberRepository"/> 的 EF 实现（F35）。租户隔离由 AppDbContext 全局过滤器保证。
/// </summary>
internal sealed class WorkspaceMemberRepository(AppDbContext context) : IWorkspaceMemberRepository
{
    public Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default) =>
        context.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken ct = default) =>
        await context.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.WorkspaceId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await context.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        context.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, ct);

    public async Task AddAsync(WorkspaceMember member, CancellationToken ct = default) =>
        await context.WorkspaceMembers.AddAsync(member, ct);

    public async Task<bool> RemoveAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var member = await context.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);
        if (member is null)
        {
            return false;
        }

        context.WorkspaceMembers.Remove(member);
        return true;
    }
}
