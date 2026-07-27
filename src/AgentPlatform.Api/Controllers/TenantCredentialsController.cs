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

    /// <summary>初始化 <see cref="TenantCredentialsController"/> 的新实例。</summary>
    public TenantCredentialsController(
        ITenantProvider tenant,
        ITenantCredentialSettingRepository repository,
        IApiKeyEncryptionService encryption,
        ITenantCredentialResolver resolver,
        IUnitOfWork unitOfWork)
    {
        _tenant = tenant;
        _repository = repository;
        _encryption = encryption;
        _resolver = resolver;
        _unitOfWork = unitOfWork;
    }

    /// <summary>获取当前租户某类凭据设置；未配置返回 204。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] CredentialCategory category,
        CancellationToken ct)
    {
        var tenantId = _tenant.GetTenantId();
        var setting = await _repository.GetByTenantAndCategoryAsync(tenantId, category, ct);
        if (setting is null)
            return NoContent();

        return Ok(Map(setting));
    }

    /// <summary>
    /// 创建或覆盖更新（upsert）当前租户某类凭据设置。
    /// 入站明文 <see cref="UpdateTenantCredentialRequest.ApiKey"/> 在服务端加密后立即丢弃；
    /// 留空则保留既有密文。成功后使该租户 + 类别的解析缓存失效，确保新密钥即时生效。
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Put(
        [FromBody] UpdateTenantCredentialRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest("Provider is required");

        var tenantId = _tenant.GetTenantId();
        var existing = await _repository.GetByTenantAndCategoryAsync(tenantId, request.Category, ct);

        // 解析密文：提供明文则加密；否则（已有配置且未提供）沿用既有密文。
        string encryptedKey;
        string keyPrefix;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            (encryptedKey, keyPrefix) = _encryption.EncryptKey(request.ApiKey);
        }
        else if (existing is not null)
        {
            encryptedKey = existing.EncryptedApiKey;
            keyPrefix = existing.ApiKeyPrefix;
        }
        else
        {
            return BadRequest("ApiKey is required when configuring a credential for the first time");
        }

        if (existing is null)
        {
            var setting = new TenantCredentialSetting(
                Guid.NewGuid(), tenantId, request.Category, request.Provider,
                encryptedKey, keyPrefix, request.BaseUrl, request.ModelName, request.IsEnabled);
            await _repository.UpsertAsync(setting, ct);
        }
        else
        {
            existing.Update(request.Provider, encryptedKey, keyPrefix,
                request.BaseUrl, request.ModelName, request.IsEnabled);
            await _repository.UpsertAsync(existing, ct);
        }

        // 提交本工作单元内的 tracked 变更。本控制器直接写仓储（未走 MediatR 命令），
        // 因此需显式提交 —— 与 UnitOfWorkBehavior 对命令处理器的 SaveChangesAsync 行为一致。
        await _unitOfWork.SaveChangesAsync(ct);

        // 配置变更即时失效缓存，避免陈旧密钥继续被使用。
        _resolver.Invalidate(tenantId, request.Category);

        var saved = await _repository.GetByTenantAndCategoryAsync(tenantId, request.Category, ct);
        return Ok(saved is null ? null : Map(saved));
    }

    private static TenantCredentialDto Map(TenantCredentialSetting s) =>
        new(
            s.Category,
            s.Provider,
            "••••" + s.ApiKeyPrefix,
            s.BaseUrl,
            s.ModelName,
            s.IsEnabled);
}
