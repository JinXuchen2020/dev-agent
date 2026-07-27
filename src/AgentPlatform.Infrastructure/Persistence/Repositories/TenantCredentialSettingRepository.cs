using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for tenant credential settings. Supports single-row upsert per tenant + category.
/// </summary>
internal sealed class TenantCredentialSettingRepository : ITenantCredentialSettingRepository
{
    private readonly AppDbContext _context;

    public TenantCredentialSettingRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TenantCredentialSetting?> GetByTenantAndCategoryAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default)
    {
        return await _context.Set<TenantCredentialSetting>()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Category == category, ct);
    }

    public async Task UpsertAsync(TenantCredentialSetting setting, CancellationToken ct = default)
    {
        var existing = await _context.Set<TenantCredentialSetting>()
            .FirstOrDefaultAsync(s => s.TenantId == setting.TenantId && s.Category == setting.Category, ct);

        if (existing is null)
        {
            _context.Set<TenantCredentialSetting>().Add(setting);
        }
        else
        {
            // Carry over the persisted row id; apply the new (encrypted) values in place.
            existing.Update(setting.Provider, setting.EncryptedApiKey, setting.ApiKeyPrefix,
                setting.BaseUrl, setting.ModelName, setting.IsEnabled);
            _context.Set<TenantCredentialSetting>().Update(existing);
        }
    }
}
