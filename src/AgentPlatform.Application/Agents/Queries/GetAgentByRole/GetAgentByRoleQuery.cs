using AgentPlatform.Domain.Aggregates.Agents;
using MediatR;

namespace AgentPlatform.Application.Agents.Queries.GetAgentByRole;

/// <summary>
/// Represents a query to retrieve agents by their role code.
/// </summary>
/// <param name="RoleCode">The role code to filter agents by (e.g., "architect", "developer").</param>
/// <param name="TenantId">The tenant to scope the query to.</param>
public sealed record GetAgentByRoleQuery(
    string RoleCode,
    Guid TenantId
) : IRequest<IReadOnlyList<Agent>>;

internal sealed class GetAgentByRoleQueryHandler(
    Domain.Repositories.IAgentRepository repository)
    : IRequestHandler<GetAgentByRoleQuery, IReadOnlyList<Agent>>
{
    public async Task<IReadOnlyList<Agent>> Handle(
        GetAgentByRoleQuery request, CancellationToken ct)
    {
        return await repository.GetByRoleAsync(request.RoleCode, ct);
    }
}
