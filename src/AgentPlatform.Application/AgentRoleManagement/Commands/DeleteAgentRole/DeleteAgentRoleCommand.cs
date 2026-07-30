using AgentPlatform.Application.Abstractions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Commands.DeleteAgentRole;

/// <summary>
/// Outcome of a <see cref="DeleteAgentRoleCommand"/>.
/// </summary>
public enum AgentRoleDeletionOutcome
{
    /// <summary>The role was deleted successfully.</summary>
    Deleted,

    /// <summary>No role with the supplied role code was found.</summary>
    NotFound,

    /// <summary>The role is a built-in platform role and cannot be deleted.</summary>
    BuiltInConflict,

    /// <summary>The role is still referenced by one or more agents and cannot be deleted.</summary>
    InUseConflict,
}

/// <summary>
/// Command to delete a custom agent role by its role code.
/// Built-in roles and roles still referenced by agents are rejected (not deleted).
/// </summary>
/// <param name="RoleCode">The unique code of the role to delete.</param>
public sealed record DeleteAgentRoleCommand(string RoleCode) : ICommand<AgentRoleDeletionOutcome>;

internal sealed class DeleteAgentRoleCommandHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository roleRepo,
    Domain.Repositories.IAgentRepository agentRepo)
    : IRequestHandler<DeleteAgentRoleCommand, AgentRoleDeletionOutcome>
{
    public async Task<AgentRoleDeletionOutcome> Handle(
        DeleteAgentRoleCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleCode);

        var role = await roleRepo.GetByRoleCodeAsync(request.RoleCode, ct);
        if (role is null)
            return AgentRoleDeletionOutcome.NotFound;

        // Built-in platform roles are read-only.
        if (role.IsBuiltIn)
            return AgentRoleDeletionOutcome.BuiltInConflict;

        // Roles still referenced by agents anywhere must not be deleted — unlink first.
        var agentsWithRole = await agentRepo.GetByRoleAsync(request.RoleCode, ct);
        if (agentsWithRole.Count != 0)
            return AgentRoleDeletionOutcome.InUseConflict;

        roleRepo.Remove(role);
        return AgentRoleDeletionOutcome.Deleted;
    }
}
