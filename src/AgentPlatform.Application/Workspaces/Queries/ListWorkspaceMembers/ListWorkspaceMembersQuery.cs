using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Queries.ListWorkspaceMembers;

/// <summary>
/// Query to list members of a workspace (Admin only, enforced at the API edge).
/// 工作空间不存在时返回 null（映射 404）。
/// </summary>
public sealed record ListWorkspaceMembersQuery(Guid WorkspaceId) : IRequest<IReadOnlyList<WorkspaceMemberDto>?>;

internal sealed class ListWorkspaceMembersQueryHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceMemberRepository memberRepo,
    IUserRepository userRepo)
    : IRequestHandler<ListWorkspaceMembersQuery, IReadOnlyList<WorkspaceMemberDto>?>
{
    public async Task<IReadOnlyList<WorkspaceMemberDto>?> Handle(ListWorkspaceMembersQuery request, CancellationToken ct)
    {
        if (await workspaceRepo.GetByIdAsync(request.WorkspaceId, ct) is null)
            return null;

        var members = await memberRepo.ListByWorkspaceAsync(request.WorkspaceId, ct);
        var result = new List<WorkspaceMemberDto>(members.Count);
        foreach (var member in members)
        {
            var user = await userRepo.GetByIdAsync(member.UserId, ct);
            result.Add(new WorkspaceMemberDto(member.UserId, user?.Email ?? "(deleted)", member.CreatedAt));
        }

        return result;
    }
}
