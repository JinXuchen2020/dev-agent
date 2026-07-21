using AgentPlatform.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Provides the current tenant identifier resolved from the HTTP request context.
/// Resolution order: JWT "tenant_id" claim → "X-Tenant-Id" header → configured default tenant.
/// </summary>
internal sealed class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantSettings _settings;
    private readonly ILogger<TenantProvider> _logger;

    public TenantProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TenantSettings> settings,
        ILogger<TenantProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the tenant identifier resolved from the current request context.
    /// </summary>
    /// <returns>
    /// Tenant ID from the JWT "tenant_id" claim if present; otherwise from the
    /// "X-Tenant-Id" header; otherwise the configured <see cref="TenantSettings.DefaultTenantId"/>.
    /// </returns>
    public Guid GetTenantId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            // Priority 1: JWT "tenant_id" claim
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            if (tenantClaim is not null && Guid.TryParse(tenantClaim, out var tenantIdFromClaim))
            {
                return tenantIdFromClaim;
            }

            // Priority 2: "X-Tenant-Id" header
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValues) &&
                headerValues.FirstOrDefault() is { } headerValue &&
                Guid.TryParse(headerValue, out var tenantIdFromHeader))
            {
                return tenantIdFromHeader;
            }
        }

        // Priority 3: Configured default tenant (backward compatibility)
        _logger.LogWarning(
            "No tenant identifier found in request context; falling back to default tenant {DefaultTenantId}.",
            _settings.DefaultTenantId);

        return _settings.DefaultTenantId;
    }
}
