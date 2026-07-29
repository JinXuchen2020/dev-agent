using AgentPlatform.Application.AgentConfigurationManagement;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;
using System.Collections;

namespace AgentPlatform.Application.AgentConfigurationManagement.Queries.GetConfigurationTemplate;

/// <summary>
/// Query to project an agent configuration into a structured, instantiation-ready
/// <see cref="ConfigurationAgentTemplate"/> by parsing its YAML content on the server.
/// </summary>
/// <param name="Id">The unique identifier of the configuration.</param>
public sealed record GetConfigurationTemplateQuery(Guid Id) : IRequest<ConfigurationAgentTemplate?>;

internal sealed class GetConfigurationTemplateQueryHandler(
    IAgentConfigurationRepository repository,
    ITenantProvider tenantProvider,
    IYamlConfigurationParser yamlParser)
    : IRequestHandler<GetConfigurationTemplateQuery, ConfigurationAgentTemplate?>
{
    public async Task<ConfigurationAgentTemplate?> Handle(
        GetConfigurationTemplateQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();
        var config = await repository.GetByIdAsync(request.Id, ct);

        // Tenant-scoped enforcement: the repository does not filter by tenant on lookup,
        // so cross-tenant ids must be rejected here (returns 404, not 403, to avoid leaking existence).
        if (config == null || config.TenantId != tenantId)
            return null;

        string? roleCode = null;
        string? modelProvider = null;
        string? modelName = null;
        string? modelApiUrl = null;
        string? systemPrompt = null;

        // YAML is validated on create/update, but parse defensively so a malformed
        // document degrades to a metadata-only template rather than a 500.
        try
        {
            var dict = yamlParser.Parse(config.YamlContent);

            roleCode = GetString(dict, "agent_role");
            systemPrompt = GetString(dict, "system_prompt");

            if (AsStringDictionary(dict.GetValueOrDefault("model")) is { } model)
            {
                modelProvider = GetString(model, "provider");
                modelName = GetString(model, "name");
                modelApiUrl = GetString(model, "api_url");
            }
        }
        catch (ArgumentException)
        {
            // Leave extracted fields null; metadata below is still returned.
        }

        return new ConfigurationAgentTemplate(
            config.Id,
            config.Name,
            config.Description,
            roleCode,
            modelProvider,
            modelName,
            modelApiUrl,
            systemPrompt,
            config.Version.ToString());
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out var value) && value is string s ? s : null;

    /// <summary>
    /// Coerces an arbitrary YAML node into a string-keyed dictionary, handling the
    /// <see cref="Dictionary{Object,Object}"/> shape YamlDotNet emits for nested mappings.
    /// </summary>
    private static IReadOnlyDictionary<string, object?>? AsStringDictionary(object? value)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnly)
            return readOnly;

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
                result[entry.Key?.ToString() ?? string.Empty] = entry.Value;
            return result;
        }

        return null;
    }
}
