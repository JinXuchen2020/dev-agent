using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.RemoveWorkspaceMember;

/// <summary>
/// Command to remove a member from a workspace (Admin only, enforced at the API edge).
/// Returns false when the workspace or the membership does not exist (mapped to 404).
/// </summary>
public sealed record RemoveWorkspaceMemberCommand(Guid WorkspaceId, Guid UserId) : ICommand<bool>;

internal sealed class RemoveWorkspaceMemberCommandHandler(
    IWorkspaceMemberRepository memberRepo)
    : IRequestHandler<RemoveWorkspaceMemberCommand, bool>
{
    public async Task<bool> Handle(RemoveWorkspaceMemberCommand request, CancellationToken ct)
    {
        return await memberRepo.RemoveAsync(request.WorkspaceId, request.UserId, ct);
    }
}
