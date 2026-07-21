namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Represents a configured API key with its associated tenant and roles.
/// </summary>
public sealed class ApiKeyConfiguration
{
    /// <summary>The API key value.</summary>
    public required string Key { get; init; }

    /// <summary>The tenant ID associated with this API key.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The roles granted by this API key.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Whether this API key is active.</summary>
    public bool IsActive { get; init; } = true;
}
