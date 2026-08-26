using AgentPlatform.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Provides the current tenant identifier resolved from the HTTP request context.
/// Resolution order: <see cref="ITenantContext.OverrideTenantId"/> (background / anonymous scope
/// injection) → JWT "tenant_id" claim → "X-Tenant-Id" header → configured default tenant.
/// </summary>
internal sealed class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantSettings _settings;
    private readonly ILogger<TenantProvider> _logger;
    private readonly ITenantContext _tenantContext;

    public TenantProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TenantSettings> settings,
        ILogger<TenantProvider> logger,
        ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings.Value;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the tenant identifier resolved from the current context.
    /// </summary>
    /// <returns>
    /// Tenant ID from the ambient <see cref="ITenantContext.OverrideTenantId"/> if set
    /// (background jobs, anonymous webhooks); otherwise from the JWT "tenant_id" claim;
    /// otherwise from the "X-Tenant-Id" header; otherwise the configured default tenant.
    /// </returns>
    public Guid GetTenantId()
    {
        // Priority 0: ambient override set by background scheduler / anonymous webhook scope.
        if (_tenantContext.OverrideTenantId is { } overridden)
            return overridden;

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

        // Priority 3: Configured default tenant
        _logger.LogWarning(
            "No tenant identifier found in request context; falling back to default tenant {DefaultTenantId}.",
            _settings.DefaultTenantId);

        return _settings.DefaultTenantId;
    }
}