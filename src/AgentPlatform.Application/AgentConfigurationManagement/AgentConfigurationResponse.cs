using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.AgentConfigurationManagement;

/// <summary>
/// Response DTO for agent configuration operations.
/// </summary>
/// <param name="Id">The unique identifier of the configuration.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The description.</param>
/// <param name="YamlContent">The YAML configuration content.</param>
/// <param name="Version">The current semantic version.</param>
/// <param name="AgentTypeCode">The agent type code, if any.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="CreatedAt">The creation timestamp.</param>
/// <param name="UpdatedAt">The last update timestamp.</param>
public sealed record AgentConfigurationResponse(
    Guid Id,
    string Name,
    string? Description,
    string YamlContent,
    string Version,
    string? AgentTypeCode,
    AgentConfigurationStatus Status,
    Guid TenantId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Summary DTO for agent configuration list results.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The description.</param>
/// <param name="Version">The current semantic version.</param>
/// <param name="AgentTypeCode">The agent type code, if any.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="UpdatedAt">The last update timestamp.</param>
public sealed record AgentConfigurationSummary(
    Guid Id,
    string Name,
    string? Description,
    string Version,
    string? AgentTypeCode,
    AgentConfigurationStatus Status,
    DateTime UpdatedAt
);

/// <summary>
/// Paginated response for agent configuration queries.
/// </summary>
/// <param name="Items">The list of configuration summaries.</param>
/// <param name="TotalCount">The total number of matching records.</param>
public sealed record AgentConfigurationListResponse(
    IReadOnlyList<AgentConfigurationSummary> Items,
    int TotalCount
);

/// <summary>
/// Structured, instantiation-ready projection of an agent configuration.
/// Consumed by the "create agent from template" frontend flow so the YAML is
/// parsed once on the server (single source of truth) rather than re-parsed on the client.
/// <para>
/// The backing YAML is expected to follow this convention (parsed fault-tolerantly;
/// any missing node leaves the corresponding field <c>null</c>):
/// <code>
/// agent_role: developer               # -> RoleCode
/// system_prompt: "You are a helpful assistant."
/// model:                              # -> ModelProvider / ModelName / ModelApiUrl
///   provider: openai
///   name: gpt-4o
///   api_url: https://api.openai.com/v1
/// </code>
/// </para>
/// </summary>
/// <param name="ConfigurationId">The identifier of the source configuration definition.</param>
/// <param name="Name">The configuration display name (used to prefill the agent name).</param>
/// <param name="Description">The configuration description, if any.</param>
/// <param name="RoleCode">The role code extracted from the YAML <c>agent_role</c> node.</param>
/// <param name="ModelProvider">The model provider extracted from the YAML <c>model.provider</c> node.</param>
/// <param name="ModelName">The model name extracted from the YAML <c>model.name</c> node.</param>
/// <param name="ModelApiUrl">The model API base URL extracted from the YAML <c>model.api_url</c> node.</param>
/// <param name="SystemPrompt">The system prompt extracted from the YAML <c>system_prompt</c> node.</param>
/// <param name="SourceVersion">The semantic version of the source configuration (e.g. "1.2.0").</param>
public sealed record ConfigurationAgentTemplate(
    Guid ConfigurationId,
    string Name,
    string? Description,
    string? RoleCode,
    string? ModelProvider,
    string? ModelName,
    string? ModelApiUrl,
    string? SystemPrompt,
    string SourceVersion
);
