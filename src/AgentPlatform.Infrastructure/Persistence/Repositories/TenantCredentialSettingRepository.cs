using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for tenant credential settings. Supports multiple credentials per tenant + category.
/// </summary>
internal sealed class TenantCredentialSettingRepository : ITenantCredentialSettingRepository
{
    private readonly AppDbContext _context;

    public TenantCredentialSettingRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TenantCredentialSetting>> GetAllByTenantAndCategoryAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default)
    {
        var list = await _context.Set<TenantCredentialSetting>()
            .Where(s => s.TenantId == tenantId && s.Category == category)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
        return list;
    }

    public async Task<TenantCredentialSetting?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _context.Set<TenantCredentialSetting>()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);
    }

    public async Task AddAsync(TenantCredentialSetting setting, CancellationToken ct = default)
    {
        await _context.Set<TenantCredentialSetting>().AddAsync(setting, ct);
    }

    public async Task UpdateAsync(TenantCredentialSetting setting, CancellationToken ct = default)
    {
        var existing = await _context.Set<TenantCredentialSetting>()
            .FirstOrDefaultAsync(s => s.TenantId == setting.TenantId && s.Id == setting.Id, ct);

        if (existing is null)
            return;

        existing.Update(setting.Name, setting.Provider, setting.EncryptedApiKey, setting.ApiKeyPrefix,
            setting.BaseUrl, setting.ModelName, setting.IsEnabled);
    }

    public async Task DeleteAsync(TenantCredentialSetting setting, CancellationToken ct = default)
    {
        _context.Set<TenantCredentialSetting>().Remove(setting);
        await Task.CompletedTask;
    }
}
