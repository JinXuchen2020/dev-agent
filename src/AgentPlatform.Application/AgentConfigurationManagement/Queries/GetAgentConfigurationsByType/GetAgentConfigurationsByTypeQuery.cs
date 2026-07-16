using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Queries.GetAgentConfigurationsByType;

/// <summary>
/// Query to retrieve all agent configurations associated with a specific agent type code.
/// </summary>
/// <param name="AgentTypeCode">The agent type code to filter by.</param>
public sealed record GetAgentConfigurationsByTypeQuery(string AgentTypeCode)
    : IRequest<IReadOnlyList<AgentConfigurationSummary>>;

internal sealed class GetAgentConfigurationsByTypeQueryHandler(
    Domain.Repositories.IAgentConfigurationRepository repository)
    : IRequestHandler<GetAgentConfigurationsByTypeQuery, IReadOnlyList<AgentConfigurationSummary>>
{
    public async Task<IReadOnlyList<AgentConfigurationSummary>> Handle(
        GetAgentConfigurationsByTypeQuery request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentTypeCode);

        var configs = await repository.GetByAgentTypeCodeAsync(request.AgentTypeCode, ct);

        return configs
            .Select(c => new AgentConfigurationSummary(
                c.Id,
                c.Name,
                c.Description,
                c.Version.ToString(),
                c.AgentTypeCode,
                c.Status,
                c.UpdatedAt))
            .ToList();
    }
}
