using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.AgentRoleManagement.Commands.CreateAgentRole;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Commands.UpdateAgentRoleDefinition;

/// <summary>
/// Command to update an existing agent role definition.
/// The role code is the immutable key (locked for built-in roles); this updates only
/// the editable metadata (name, description, system prompt) of the role identified by <see cref="RoleCode"/>.
/// </summary>
/// <param name="RoleCode">The unique code identifying the role to update (immutable).</param>
/// <param name="Name">The new display name.</param>
/// <param name="Description">The new description (optional).</param>
/// <param name="SystemPrompt">The new system prompt.</param>
public sealed record UpdateAgentRoleDefinitionCommand(
    string RoleCode,
    string Name,
    string? Description,
    string SystemPrompt
) : ICommand<AgentRoleResponse?>;

internal sealed class UpdateAgentRoleDefinitionCommandHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository repository)
    : IRequestHandler<UpdateAgentRoleDefinitionCommand, AgentRoleResponse?>
{
    public async Task<AgentRoleResponse?> Handle(
        UpdateAgentRoleDefinitionCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);

        var role = await repository.GetByRoleCodeAsync(request.RoleCode, ct);
        if (role is null)
            return null;

        // RoleCode is the immutable key — never reassigned, for both built-in and custom roles.
        role.UpdateMetadata(request.Name, request.Description ?? string.Empty, request.SystemPrompt);
        repository.Update(role);

        return new AgentRoleResponse(
            role.Id,
            role.Name,
            role.RoleCode,
            role.Description,
            role.SystemPrompt,
            role.IsBuiltIn);
    }
}
