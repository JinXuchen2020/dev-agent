using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Commands.CreateAgentRole;

/// <summary>
/// Command to create a new custom agent role definition.
/// </summary>
/// <param name="Name">The display name of the role.</param>
/// <param name="RoleCode">The unique code identifying this role.</param>
/// <param name="Description">A description of the role's responsibilities.</param>
/// <param name="SystemPrompt">The system prompt used by agents assigned to this role.</param>
public sealed record CreateAgentRoleCommand(
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt
) : ICommand<AgentRoleResponse>;

/// <summary>
/// Response containing the created agent role details.
/// </summary>
/// <param name="Id">The unique identifier of the role.</param>
/// <param name="Name">The display name.</param>
/// <param name="RoleCode">The unique code.</param>
/// <param name="Description">The description.</param>
/// <param name="SystemPrompt">The system prompt.</param>
public sealed record AgentRoleResponse(
    Guid Id,
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt
);

internal sealed class CreateAgentRoleCommandHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository repository)
    : IRequestHandler<CreateAgentRoleCommand, AgentRoleResponse>
{
    public Task<AgentRoleResponse> Handle(
        CreateAgentRoleCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);

        var definition = new AgentRoleDefinition(
            Guid.NewGuid(),
            request.Name,
            request.RoleCode,
            request.Description,
            request.SystemPrompt);

        repository.Add(definition);

        return Task.FromResult(new AgentRoleResponse(
            definition.Id,
            definition.Name,
            definition.RoleCode,
            definition.Description,
            definition.SystemPrompt));
    }
}
