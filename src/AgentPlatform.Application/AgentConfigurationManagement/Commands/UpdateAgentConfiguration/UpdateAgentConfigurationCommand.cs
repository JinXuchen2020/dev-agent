using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Commands.UpdateAgentConfiguration;

/// <summary>
/// Command to update an existing agent configuration's YAML content and metadata.
/// </summary>
/// <param name="Id">The unique identifier of the configuration to update.</param>
/// <param name="YamlContent">The updated YAML content.</param>
/// <param name="ChangeLog">A description of the changes in this version.</param>
/// <param name="VersionBump">The type of version bump to apply (default: Patch).</param>
/// <param name="Name">Optional new display name.</param>
/// <param name="Description">Optional new description.</param>
public sealed record UpdateAgentConfigurationCommand(
    Guid Id,
    string YamlContent,
    string? ChangeLog = null,
    VersionBump VersionBump = VersionBump.Patch,
    string? Name = null,
    string? Description = null
) : ICommand<AgentConfigurationResponse?>;

internal sealed class UpdateAgentConfigurationCommandHandler(
    Domain.Repositories.IAgentConfigurationRepository repository,
    Application.Abstractions.IYamlConfigurationParser yamlParser)
    : IRequestHandler<UpdateAgentConfigurationCommand, AgentConfigurationResponse?>
{
    public async Task<AgentConfigurationResponse?> Handle(
        UpdateAgentConfigurationCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.YamlContent);

        var config = await repository.GetByIdAsync(request.Id, ct);
        if (config == null)
            return null;

        // Validate YAML
        if (!yamlParser.Validate(request.YamlContent))
            throw new ArgumentException("The provided YAML content is not valid YAML.", nameof(request.YamlContent));

        // Update content (bumps version)
        config.UpdateContent(request.YamlContent, request.ChangeLog, request.VersionBump);

        // Update name if provided
        if (!string.IsNullOrWhiteSpace(request.Name))
            config.UpdateName(request.Name);

        // Update description if provided
        if (request.Description != null)
            config.UpdateDescription(request.Description);

        repository.Update(config);

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
