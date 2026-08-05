using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for API key aggregates.
/// Supports active key lookup, rotation, expiry scanning, and lifecycle management.
/// </summary>
internal sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly AppDbContext _context;

    public ApiKeyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ApiKey>> GetActiveKeysAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Set<ApiKey>()
            .Where(k => k.TenantId == tenantId
                && k.IsActive
                && (k.ExpiresAt == null || k.ExpiresAt > now)
                && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<ApiKey>().FindAsync([id], ct);
    }

    public async Task<IReadOnlyList<ApiKey>> GetAllActiveKeysAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // 跨租户查询：API-Key 认证本身不预知租户（请求仅带 X-API-Key，无 JWT / X-Tenant-Id），
        // 必须绕过 AppDbContext 的全局租户查询过滤器（该过滤器在此时会按 DefaultTenantId 收窄，
        // 导致非默认租户的密钥永远无法被匹配 → 401）。此方法是全局密钥扫描的唯一入口，
        // 仅被 ApiKeyAuthenticationHandler 使用，忽略过滤器不会破坏任何租户隔离语义。
        return await _context.Set<ApiKey>()
            .IgnoreQueryFilters()
            .Where(k => k.IsActive
                && (k.ExpiresAt == null || k.ExpiresAt > now)
                && k.RevokedAt == null)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKey>> GetExpiringKeysAsync(int withinDays, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiryThreshold = now.AddDays(withinDays);
        return await _context.Set<ApiKey>()
            .Where(k => k.IsActive
                && k.RevokedAt == null
                && k.ExpiresAt != null
                && k.ExpiresAt > now
                && k.ExpiresAt <= expiryThreshold)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKey>> GetExpiredActiveKeysAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Set<ApiKey>()
            .Where(k => k.IsActive
                && k.RevokedAt == null
                && k.ExpiresAt != null
                && k.ExpiresAt <= now)
            .ToListAsync(ct);
    }

    public void Add(ApiKey apiKey)
    {
        _context.Set<ApiKey>().Add(apiKey);
    }

    public void Update(ApiKey apiKey)
    {
        _context.Set<ApiKey>().Update(apiKey);
    }

    public Task UpdateAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        _context.Set<ApiKey>().Update(apiKey);
        return Task.CompletedTask;
    }
}
