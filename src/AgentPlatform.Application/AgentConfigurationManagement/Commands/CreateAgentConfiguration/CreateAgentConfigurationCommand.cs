using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.ValueObjects;
using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Commands.CreateAgentConfiguration;

/// <summary>
/// Command to create a new agent configuration.
/// </summary>
/// <param name="Name">The display name of the configuration.</param>
/// <param name="YamlContent">The YAML content defining the agent configuration.</param>
/// <param name="Description">An optional description of the configuration's purpose.</param>
/// <param name="AgentTypeCode">An optional role code this configuration is intended for.</param>
public sealed record CreateAgentConfigurationCommand(
    string Name,
    string YamlContent,
    string? Description = null,
    string? AgentTypeCode = null
) : ICommand<AgentConfigurationResponse>;

internal sealed class CreateAgentConfigurationCommandHandler(
    Domain.Repositories.IAgentConfigurationRepository repository,
    Application.Abstractions.ITenantProvider tenantProvider,
    Application.Abstractions.IYamlConfigurationParser yamlParser)
    : IRequestHandler<CreateAgentConfigurationCommand, AgentConfigurationResponse>
{
    public Task<AgentConfigurationResponse> Handle(
        CreateAgentConfigurationCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.YamlContent);

        // Validate YAML content is parseable
        if (!yamlParser.Validate(request.YamlContent))
            throw new InvalidYamlException(nameof(request.YamlContent));

        var configuration = new AgentConfiguration(
            Guid.NewGuid(),
            request.Name,
            request.YamlContent,
            tenantProvider.GetTenantId(),
            version: ConfigurationVersion.Initial,
            description: request.Description,
            agentTypeCode: request.AgentTypeCode);

        repository.Add(configuration);

        return Task.FromResult(new AgentConfigurationResponse(
            configuration.Id,
            configuration.Name,
            configuration.Description,
            configuration.YamlContent,
            configuration.Version.ToString(),
            configuration.AgentTypeCode,
            configuration.Status,
            configuration.TenantId,
            configuration.CreatedAt,
            configuration.UpdatedAt));
    }
}
