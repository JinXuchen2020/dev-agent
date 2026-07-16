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
