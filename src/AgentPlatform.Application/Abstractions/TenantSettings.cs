namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains tenant-level configuration settings for the application.
/// Currently only used during database seeding to specify the default tenant ID.
/// Runtime tenant resolution uses JWT claims / DB lookup, not this config.
/// </summary>
public sealed record TenantSettings
{
    /// <summary>
    /// Gets or initializes the default tenant identifier used during database seeding.
    /// If not configured, the hardcoded seed tenant (00000000-0000-0000-0000-000000000001) is used.
    /// </summary>
    public Guid DefaultTenantId { get; init; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
