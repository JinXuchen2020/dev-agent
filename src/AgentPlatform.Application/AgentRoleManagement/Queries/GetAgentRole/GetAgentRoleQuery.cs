using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Queries.GetAgentRole;

/// <summary>
/// Query to retrieve a custom agent role by its role code.
/// </summary>
/// <param name="RoleCode">The unique code of the role to retrieve.</param>
public sealed record GetAgentRoleQuery(string RoleCode) : IRequest<AgentRoleSummary?>;

/// <summary>
/// Summary representation of an <see cref="AgentRoleDefinition"/>.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="RoleCode">The unique code.</param>
/// <param name="Description">The description.</param>
/// <param name="SystemPrompt">The system prompt used by agents assigned to this role.</param>
/// <param name="IsBuiltIn">Whether this is a platform built-in role (read-only, non-deletable).</param>
public sealed record AgentRoleSummary(
    Guid Id,
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt,
    bool IsBuiltIn);

internal sealed class GetAgentRoleQueryHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository repository)
    : IRequestHandler<GetAgentRoleQuery, AgentRoleSummary?>
{
    public async Task<AgentRoleSummary?> Handle(
        GetAgentRoleQuery request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleCode);

        var role = await repository.GetByRoleCodeAsync(request.RoleCode, ct);
        if (role == null)
            return null;

        return new AgentRoleSummary(
            role.Id,
            role.Name,
            role.RoleCode,
            role.Description,
            role.SystemPrompt,
            role.IsBuiltIn);
    }
}
