using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.DeleteWorkspace;

/// <summary>Outcome of a <see cref="DeleteWorkspaceCommand"/>.</summary>
public enum WorkspaceDeletionOutcome
{
    /// <summary>The workspace was deleted successfully.</summary>
    Deleted,

    /// <summary>No workspace with the supplied id was found in the tenant.</summary>
    NotFound,

    /// <summary>The default workspace cannot be deleted.</summary>
    DefaultConflict,

    /// <summary>The workspace still contains members or business entities and cannot be deleted.</summary>
    InUseConflict,
}

/// <summary>
/// Command to delete an empty, non-default workspace (Admin only, enforced at the API edge).
/// 守卫（决策 D4，固定红线）：默认工作空间恒不可删；仍有成员或业务实体 → InUseConflict；
/// 绝不级联删除/移动任何数据。
/// </summary>
public sealed record DeleteWorkspaceCommand(Guid Id) : ICommand<WorkspaceDeletionOutcome>;

internal sealed class DeleteWorkspaceCommandHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceMemberRepository memberRepo)
    : IRequestHandler<DeleteWorkspaceCommand, WorkspaceDeletionOutcome>
{
    public async Task<WorkspaceDeletionOutcome> Handle(DeleteWorkspaceCommand request, CancellationToken ct)
    {
        var workspace = await workspaceRepo.GetByIdAsync(request.Id, ct);
        if (workspace is null)
            return WorkspaceDeletionOutcome.NotFound;

        if (workspace.IsDefault)
            return WorkspaceDeletionOutcome.DefaultConflict;

        if (await memberRepo.CountByWorkspaceAsync(workspace.Id, ct) > 0)
            return WorkspaceDeletionOutcome.InUseConflict;

        if (await workspaceRepo.CountBusinessEntitiesAsync(workspace.Id, ct) > 0)
            return WorkspaceDeletionOutcome.InUseConflict;

        workspaceRepo.Remove(workspace);
        return WorkspaceDeletionOutcome.Deleted;
    }
}
