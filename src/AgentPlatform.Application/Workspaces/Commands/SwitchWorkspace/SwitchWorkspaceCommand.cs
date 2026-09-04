using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.SwitchWorkspace;

/// <summary>
/// Command to switch the caller's active workspace (决策 D1=C)：校验目标工作空间在当前租户内
/// 且对调用者可见（Admin 全部可见；非 Admin 需为成员或默认工作空间），返回工作空间信息，
/// 由 API 层重签 JWT（workspace_id claim）+ httpOnly cookie。
/// 不可见时返回 null（映射 404，不泄漏存在性）。
/// </summary>
public sealed record SwitchWorkspaceCommand(Guid WorkspaceId, Guid UserId, bool IsAdmin)
    : ICommand<WorkspaceDto?>;

internal sealed class SwitchWorkspaceCommandHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceMemberRepository memberRepo)
    : IRequestHandler<SwitchWorkspaceCommand, WorkspaceDto?>
{
    public async Task<WorkspaceDto?> Handle(SwitchWorkspaceCommand request, CancellationToken ct)
    {
        var workspace = await workspaceRepo.GetByIdAsync(request.WorkspaceId, ct);
        if (workspace is null)
            return null;

        if (!request.IsAdmin &&
            !workspace.IsDefault &&
            !await memberRepo.IsMemberAsync(workspace.Id, request.UserId, ct))
        {
            return null;
        }

        return new WorkspaceDto(workspace.Id, workspace.Name, workspace.Description, workspace.IsDefault, workspace.CreatedAt);
    }
}
