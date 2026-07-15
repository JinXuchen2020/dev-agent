using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Provides the current tenant identifier from configured tenant settings for multi-tenant data isolation.
/// </summary>
internal sealed class TenantProvider : ITenantProvider
{
    private readonly TenantSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantProvider"/> class.
    /// </summary>
    /// <param name="settings">The configured tenant settings containing the default tenant identifier.</param>
    public TenantProvider(IOptions<TenantSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Returns the tenant identifier resolved from configuration.
    /// </summary>
    /// <returns>The unique identifier of the default tenant.</returns>
    public Guid GetTenantId() => _settings.DefaultTenantId;
}
