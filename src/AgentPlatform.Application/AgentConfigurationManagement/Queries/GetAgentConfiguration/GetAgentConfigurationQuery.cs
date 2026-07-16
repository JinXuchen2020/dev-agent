using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Queries.GetAgentConfiguration;

/// <summary>
/// Query to retrieve an agent configuration by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the configuration.</param>
public sealed record GetAgentConfigurationQuery(Guid Id) : IRequest<AgentConfigurationResponse?>;

internal sealed class GetAgentConfigurationQueryHandler(
    Domain.Repositories.IAgentConfigurationRepository repository)
    : IRequestHandler<GetAgentConfigurationQuery, AgentConfigurationResponse?>
{
    public async Task<AgentConfigurationResponse?> Handle(
        GetAgentConfigurationQuery request, CancellationToken ct)
    {
        var config = await repository.GetByIdAsync(request.Id, ct);
        if (config == null)
            return null;

        return new AgentConfigurationResponse(
            config.Id,
            config.Name,
            config.Description,
            config.YamlContent,
            config.Version.ToString(),
            config.AgentTypeCode,
            config.Status,
            config.TenantId,
            config.CreatedAt,
            config.UpdatedAt);
    }
}
