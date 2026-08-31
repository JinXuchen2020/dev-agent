using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.CreateWorkspace;

/// <summary>Outcome of a <see cref="CreateWorkspaceCommand"/>.</summary>
public enum CreateWorkspaceOutcome
{
    /// <summary>Workspace created successfully.</summary>
    Created,

    /// <summary>A workspace with the same name already exists in the tenant.</summary>
    NameConflict,
}

/// <summary>Result of <see cref="CreateWorkspaceCommand"/> (DTO populated on success).</summary>
public sealed record CreateWorkspaceResult(CreateWorkspaceOutcome Outcome, WorkspaceDto? Workspace);

/// <summary>
/// Command to create a new workspace in the current tenant (Admin only, enforced at the API edge).
/// </summary>
public sealed record CreateWorkspaceCommand(string Name, string? Description)
    : ICommand<CreateWorkspaceResult>;

internal sealed class CreateWorkspaceCommandHandler(
    IWorkspaceRepository workspaceRepo,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateWorkspaceCommand, CreateWorkspaceResult>
{
    public async Task<CreateWorkspaceResult> Handle(CreateWorkspaceCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        if (await workspaceRepo.NameExistsAsync(request.Name, ct))
            return new CreateWorkspaceResult(CreateWorkspaceOutcome.NameConflict, null);

        var workspace = new Domain.Aggregates.Workspaces.Workspace(
            Guid.NewGuid(), tenantProvider.GetTenantId(), request.Name, request.Description);
        await workspaceRepo.AddAsync(workspace, ct);

        return new CreateWorkspaceResult(
            CreateWorkspaceOutcome.Created,
            new WorkspaceDto(workspace.Id, workspace.Name, workspace.Description, workspace.IsDefault, workspace.CreatedAt));
    }
}
