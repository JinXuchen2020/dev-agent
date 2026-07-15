namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains tenant-level configuration settings for the application.
/// </summary>
public sealed record TenantSettings
{
    /// <summary>
    /// Gets or initializes the default tenant identifier used when no tenant is explicitly resolved.
    /// </summary>
    public Guid DefaultTenantId { get; set; }
}
