using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.UpdateWorkspace;

/// <summary>Outcome of an <see cref="UpdateWorkspaceCommand"/>.</summary>
public enum UpdateWorkspaceOutcome
{
    /// <summary>Workspace updated successfully.</summary>
    Updated,

    /// <summary>No workspace with the supplied id was found in the tenant.</summary>
    NotFound,

    /// <summary>A workspace with the target name already exists in the tenant.</summary>
    NameConflict,
}

/// <summary>Result of <see cref="UpdateWorkspaceCommand"/> (DTO populated on success).</summary>
public sealed record UpdateWorkspaceResult(UpdateWorkspaceOutcome Outcome, WorkspaceDto? Workspace);

/// <summary>
/// Command to rename / re-describe a workspace (Admin only, enforced at the API edge).
/// </summary>
public sealed record UpdateWorkspaceCommand(Guid Id, string Name, string? Description)
    : ICommand<UpdateWorkspaceResult>;

internal sealed class UpdateWorkspaceCommandHandler(
    IWorkspaceRepository workspaceRepo)
    : IRequestHandler<UpdateWorkspaceCommand, UpdateWorkspaceResult>
{
    public async Task<UpdateWorkspaceResult> Handle(UpdateWorkspaceCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var workspace = await workspaceRepo.GetByIdAsync(request.Id, ct);
        if (workspace is null)
            return new UpdateWorkspaceResult(UpdateWorkspaceOutcome.NotFound, null);

        if (!string.Equals(workspace.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
            await workspaceRepo.NameExistsAsync(request.Name, ct))
        {
            return new UpdateWorkspaceResult(UpdateWorkspaceOutcome.NameConflict, null);
        }

        workspace.Update(request.Name, request.Description);
        return new UpdateWorkspaceResult(
            UpdateWorkspaceOutcome.Updated,
            new WorkspaceDto(workspace.Id, workspace.Name, workspace.Description, workspace.IsDefault, workspace.CreatedAt));
    }
}
