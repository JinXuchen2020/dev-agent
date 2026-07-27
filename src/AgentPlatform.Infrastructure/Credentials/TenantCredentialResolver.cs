using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Credentials;

/// <summary>
/// Resolves a tenant's stored credential setting from the repository, with a short-lived in-memory cache.
/// Cache entries hold only the encrypted entity (ciphertext) — never plaintext — and are explicitly
/// invalidated on credential updates so a changed key takes effect immediately.
/// </summary>
internal sealed class TenantCredentialResolver : ITenantCredentialResolver
{
    private readonly ITenantCredentialSettingRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantCredentialResolver> _logger;
    private static readonly TimeSpan CacheSliding = TimeSpan.FromMinutes(5);

    public TenantCredentialResolver(
        ITenantCredentialSettingRepository repository,
        IMemoryCache cache,
        ILogger<TenantCredentialResolver> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    private static string CacheKey(Guid tenantId, CredentialCategory category) => $"tcs:{tenantId}:{category}";

    public async Task<IReadOnlyList<TenantCredentialSetting>> ResolveAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default)
    {
        var key = CacheKey(tenantId, category);
        if (_cache.TryGetValue<List<TenantCredentialSetting>>(key, out var cached) && cached is not null)
            return cached;

        var settings = (await _repository.GetAllByTenantAndCategoryAsync(tenantId, category, ct)).ToList();
        _cache.Set(key, settings, CacheSliding);

        return settings;
    }

    public void Invalidate(Guid tenantId, CredentialCategory category)
    {
        _cache.Remove(CacheKey(tenantId, category));
        _logger.LogInformation("Invalidated tenant credential cache for tenant {TenantId} category {Category}", tenantId, category);
    }
}
