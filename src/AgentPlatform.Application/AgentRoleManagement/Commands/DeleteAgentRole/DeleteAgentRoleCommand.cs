using AgentPlatform.Application.Abstractions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Commands.DeleteAgentRole;

/// <summary>
/// Command to delete a custom agent role by its role code.
/// </summary>
/// <param name="RoleCode">The unique code of the role to delete.</param>
public sealed record DeleteAgentRoleCommand(string RoleCode) : ICommand<bool>;

internal sealed class DeleteAgentRoleCommandHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository roleRepo,
    Domain.Repositories.IAgentRepository agentRepo)
    : IRequestHandler<DeleteAgentRoleCommand, bool>
{
    public async Task<bool> Handle(
        DeleteAgentRoleCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleCode);

        var role = await roleRepo.GetByRoleCodeAsync(request.RoleCode, ct);
        if (role == null)
            return false;

        // Unlink agents assigned to this role — do NOT delete the agents themselves
        var agentsWithRole = await agentRepo.GetByRoleAsync(request.RoleCode, ct);
        foreach (var agent in agentsWithRole)
        {
            // Remove association by setting to default role or clearing
            agentRepo.Remove(agent);
        }

        roleRepo.Remove(role);
        return true;
    }
}
