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
/// <param name="SystemPrompt">The system prompt used by agents assigned to this role.</param>
/// <param name="IsBuiltIn">Whether this is a platform built-in role (read-only, non-deletable).</param>
/// <param name="AgentCount">Number of agents in the current tenant assigned to this role.</param>
public sealed record AgentRoleSummary(
    Guid Id,
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt,
    bool IsBuiltIn,
    int AgentCount);

internal sealed class ListAgentRolesQueryHandler(
    Domain.Repositories.IAgentRoleDefinitionRepository roleRepository,
    Domain.Repositories.IAgentRepository agentRepository,
    Application.Abstractions.ITenantProvider tenantProvider)
    : IRequestHandler<ListAgentRolesQuery, IReadOnlyList<AgentRoleSummary>>
{
    public async Task<IReadOnlyList<AgentRoleSummary>> Handle(
        ListAgentRolesQuery request, CancellationToken ct)
    {
        var roles = await roleRepository.GetAllAsync(ct);
        var tenantId = tenantProvider.GetTenantId();

        var summaries = new List<AgentRoleSummary>();
        foreach (var r in roles)
        {
            var agentCount = await agentRepository.CountByRoleAsync(tenantId, r.RoleCode, ct);
            summaries.Add(new AgentRoleSummary(
                r.Id,
                r.Name,
                r.RoleCode,
                r.Description,
                r.SystemPrompt,
                r.IsBuiltIn,
                agentCount));
        }

        return summaries;
    }
}
