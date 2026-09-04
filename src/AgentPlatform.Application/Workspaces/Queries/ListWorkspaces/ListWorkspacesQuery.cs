using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Queries.ListWorkspaces;

/// <summary>
/// Query to list workspaces visible to the caller（决策 D3=B）：Admin 可见租户全部工作空间；
/// 非 Admin 仅见「默认工作空间 + 自己已加入的工作空间」。
/// UserId / IsAdmin 由 API 边缘从当前认证主体解析后传入（Application 无 ICurrentUser 抽象，与既有约定一致）。
/// </summary>
public sealed record ListWorkspacesQuery(Guid UserId, bool IsAdmin) : IRequest<IReadOnlyList<WorkspaceDto>>;

internal sealed class ListWorkspacesQueryHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceMemberRepository memberRepo)
    : IRequestHandler<ListWorkspacesQuery, IReadOnlyList<WorkspaceDto>>
{
    public async Task<IReadOnlyList<WorkspaceDto>> Handle(ListWorkspacesQuery request, CancellationToken ct)
    {
        if (request.IsAdmin)
        {
            var all = await workspaceRepo.ListAsync(ct);
            return all.Select(ToDto).ToList();
        }

        var defaultWorkspace = await workspaceRepo.GetDefaultAsync(ct);
        var joinedIds = await memberRepo.ListWorkspaceIdsForUserAsync(request.UserId, ct);
        var visibleIds = joinedIds.ToHashSet();
        if (defaultWorkspace is not null)
            visibleIds.Add(defaultWorkspace.Id);

        var visible = await workspaceRepo.ListByIdsAsync(visibleIds, ct);
        return visible.Select(ToDto).ToList();
    }

    private static WorkspaceDto ToDto(Domain.Aggregates.Workspaces.Workspace w) =>
        new(w.Id, w.Name, w.Description, w.IsDefault, w.CreatedAt);
}
