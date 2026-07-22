using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Queries.ListAgentConfigurations;

/// <summary>
/// Query to list agent configurations with optional status and pagination.
/// </summary>
/// <param name="Status">Optional filter by configuration status.</param>
/// <param name="Skip">Number of records to skip (default: 0).</param>
/// <param name="Take">Number of records to take (default: 20, max: 100).</param>
public sealed record ListAgentConfigurationsQuery(
    AgentConfigurationStatus? Status = null,
    int Skip = 0,
    int Take = 20
) : IRequest<AgentConfigurationListResponse>;

internal sealed class ListAgentConfigurationsQueryHandler(
    Domain.Repositories.IAgentConfigurationRepository repository,
    Application.Abstractions.ITenantProvider tenantProvider)
    : IRequestHandler<ListAgentConfigurationsQuery, AgentConfigurationListResponse>
{
    public async Task<AgentConfigurationListResponse> Handle(
        ListAgentConfigurationsQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 100);

        var (items, totalCount) = await repository.QueryAsync(
            tenantProvider.GetTenantId(),
            status: request.Status,
            skip: request.Skip,
            take: take,
            ct: ct);

        var summaries = items
            .Select(c => new AgentConfigurationSummary(
                c.Id,
                c.Name,
                c.Description,
                c.Version.ToString(),
                c.AgentTypeCode,
                c.Status,
                c.UpdatedAt))
            .ToList();

        return new AgentConfigurationListResponse(summaries, totalCount);
    }
}
