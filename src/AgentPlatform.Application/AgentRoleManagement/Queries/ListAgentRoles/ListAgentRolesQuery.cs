using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using MediatR;

namespace AgentPlatform.Application.AgentRoleManagement.Queries.ListAgentRoles;

/// <summary>
/// Query to retrieve all custom agent role definitions.
/// </summary>
public sealed record ListAgentRolesQuery : IRequest<IReadOnlyList<AgentRoleSummary>>;

/// <summary>
/// Summary representation of an <see cref="AgentRoleDefinition"/>.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="RoleCode">The unique code.</param>
/// <param name="Description">The description.</param>
public sealed record AgentRoleSummary(
    Guid Id,
    string Name,
    string RoleCode,
    string Description);

internal sealed class ListAgentRolesQueryHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository repository)
    : IRequestHandler<ListAgentRolesQuery, IReadOnlyList<AgentRoleSummary>>
{
    public async Task<IReadOnlyList<AgentRoleSummary>> Handle(
        ListAgentRolesQuery request, CancellationToken ct)
    {
        var roles = await repository.GetAllAsync(ct);
        return roles.Select(r => new AgentRoleSummary(
            r.Id,
            r.Name,
            r.RoleCode,
            r.Description)).ToList();
    }
}
