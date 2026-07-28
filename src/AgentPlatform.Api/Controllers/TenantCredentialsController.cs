using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 多租户外部凭据配置端点（模型 + 搜索，BYO-Key / 平台内置）。
/// 一个租户可配置多个同类凭据（如多个不同模型），以列表形式返回；支持新增 / 更新 / 删除。
/// 密钥属高敏数据，仅限 Admin / Operator 操作；所有响应**绝不**返回明文密钥。
/// </summary>
[Authorize(Roles = "Admin,Operator")]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/tenant/credentials")]
public sealed class TenantCredentialsController : ControllerBase
{
    private readonly ITenantProvider _tenant;
    private readonly ITenantCredentialSettingRepository _repository;
    private readonly IApiKeyEncryptionService _encryption;
    private readonly ITenantCredentialResolver _resolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProviderModelDiscovery _modelDiscovery;

    /// <summary>初始化 <see cref="TenantCredentialsController"/> 的新实例。</summary>
    public TenantCredentialsController(
        ITenantProvider tenant,
        ITenantCredentialSettingRepository repository,
        IApiKeyEncryptionService encryption,
        ITenantCredentialResolver resolver,
        IUnitOfWork unitOfWork,
        IProviderModelDiscovery modelDiscovery)
    {
        _tenant = tenant;
        _repository = repository;
        _encryption = encryption;
        _resolver = resolver;
        _unitOfWork = unitOfWork;
        _modelDiscovery = modelDiscovery;
    }

    /// <summary>获取当前租户某类凭据设置列表（可能为空数组）。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] CredentialCategory category,
        CancellationToken ct)
    {
        var tenantId = _tenant.GetTenantId();
        var settings = await _repository.GetAllByTenantAndCategoryAsync(tenantId, category, ct);
        return Ok(settings.Select(Map));
    }

    /// <summary>
    /// 新增一条凭据设置。明文 <see cref="CreateTenantCredentialRequest.ApiKey"/> 在服务端加密后立即丢弃，绝不落库/回显。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] CreateTenantCredentialRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest("Provider is required");
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest("ApiKey is required when creating a new credential");

        var tenantId = _tenant.GetTenantId();
        var (encryptedKey, keyPrefix) = _encryption.EncryptKey(request.ApiKey);

        var setting = new TenantCredentialSetting(
            Guid.NewGuid(), tenantId, request.Category, request.Name, request.Provider,
            encryptedKey, keyPrefix, request.BaseUrl, request.ModelName, request.IsEnabled);
        await _repository.AddAsync(setting, ct);

        // 直接写仓储（未走 MediatR 命令），需显式提交，与 UnitOfWorkBehavior 对命令处理器的 SaveChangesAsync 行为一致。
        await _unitOfWork.SaveChangesAsync(ct);

        // 配置变更即时失效缓存，避免陈旧密钥继续被使用。
        _resolver.Invalidate(tenantId, request.Category);

        var saved = await _repository.GetByIdAsync(tenantId, setting.Id, ct);
        return Ok(saved is null ? null : Map(saved));
    }

    /// <summary>
    /// 按 Id 更新一条凭据设置。明文 <see cref="UpdateTenantCredentialRequest.ApiKey"/> 仅服务端使用，加密后即刻丢弃；
    /// 留空则保留既有密文。成功后使该租户 + 类别的解析缓存失效，确保新密钥即时生效。
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Put(
        Guid id,
        [FromBody] UpdateTenantCredentialRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest("Provider is required");

        var tenantId = _tenant.GetTenantId();
        var existing = await _repository.GetByIdAsync(tenantId, id, ct);
        if (existing is null)
            return NotFound();

        // 解析密文：提供明文则加密；否则（已有配置且未提供）沿用既有密文。
        string encryptedKey;
        string keyPrefix;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            (encryptedKey, keyPrefix) = _encryption.EncryptKey(request.ApiKey);
        }
        else
        {
            encryptedKey = existing.EncryptedApiKey;
            keyPrefix = existing.ApiKeyPrefix;
        }

        existing.Update(request.Name, request.Provider, encryptedKey, keyPrefix,
            request.BaseUrl, request.ModelName, request.IsEnabled);
        await _repository.UpdateAsync(existing, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        _resolver.Invalidate(tenantId, request.Category);

        var saved = await _repository.GetByIdAsync(tenantId, existing.Id, ct);
        return Ok(saved is null ? null : Map(saved));
    }

    /// <summary>按 Id 删除一条凭据设置。</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.GetTenantId();
        var existing = await _repository.GetByIdAsync(tenantId, id, ct);
        if (existing is null)
            return NotFound();

        await _repository.DeleteAsync(existing, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _resolver.Invalidate(tenantId, existing.Category);

        return NoContent();
    }

    /// <summary>
    /// 探测供应商账户下所有可访问模型（OpenAI 兼容 GET /models），供「拉取模型」下拉免去手填模型名。
    /// <see cref="DiscoverModelsRequest.ApiKey"/> 仅用于本次一次性出站探测，绝不落库、绝不写日志。
    /// </summary>
    [HttpPost("discover-models")]
    public async Task<IActionResult> DiscoverModels(
        [FromBody] DiscoverModelsRequest request,
        CancellationToken ct)
    {
        try
        {
            var models = await _modelDiscovery.DiscoverAsync(request.Provider, request.ApiKey, request.BaseUrl, ct);
            return Ok(models);
        }
        catch (ProviderModelDiscoveryException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static TenantCredentialDto Map(TenantCredentialSetting s) =>
        new(
            s.Id,
            s.Name,
            s.Category,
            s.Provider,
            "••••" + s.ApiKeyPrefix,
            s.BaseUrl,
            s.ModelName,
            s.IsEnabled);
}
