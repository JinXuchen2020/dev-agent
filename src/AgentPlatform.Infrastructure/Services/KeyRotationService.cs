using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// Internal service that rotates an API key: generates fresh encrypted key material,
/// advances the aggregate's <see cref="ApiKey.KeyVersion"/>, persists the change, and
/// records a <see cref="AuditActionType.KeyRotation"/> audit entry.
/// Not exposed as a public HTTP endpoint.
/// </summary>
internal sealed class KeyRotationService : IKeyRotationService
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<KeyRotationService> _logger;

    public KeyRotationService(
        IApiKeyRepository apiKeyRepository,
        IApiKeyEncryptionService encryptionService,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<KeyRotationService> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _encryptionService = encryptionService;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task RotateKeyAsync(Guid apiKeyId, CancellationToken ct = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(apiKeyId, ct);
        if (apiKey is null)
        {
            _logger.LogWarning("API key rotation skipped: key {KeyId} not found.", apiKeyId);
            return;
        }

        var newPlaintextKey = GenerateSecureKey();
        var (encryptedKey, _) = _encryptionService.EncryptKey(newPlaintextKey);

        apiKey.Rotate(encryptedKey);
        await _apiKeyRepository.UpdateAsync(apiKey, ct);

        var auditLog = AuditLog.Record(
            tenantId: apiKey.TenantId,
            action: AuditActionType.KeyRotation,
            entity: "ApiKey",
            userId: null,
            entityId: apiKey.Id,
            details: $"Rotated API key '{apiKey.KeyPrefix}' to version {apiKey.KeyVersion}.");
        _auditLogRepository.Add(auditLog);

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "API key {KeyId} rotated to version {Version} for tenant {TenantId}",
            apiKeyId, apiKey.KeyVersion, apiKey.TenantId);
    }

    public async Task RevokeKeyAsync(Guid apiKeyId, CancellationToken ct = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(apiKeyId, ct);
        if (apiKey is null)
        {
            _logger.LogWarning("API key revocation skipped: key {KeyId} not found.", apiKeyId);
            return;
        }

        if (!apiKey.IsActive)
        {
            // Already revoked/inactive — nothing to do, keep the operation idempotent.
            return;
        }

        apiKey.Revoke();
        await _apiKeyRepository.UpdateAsync(apiKey, ct);

        var auditLog = AuditLog.Record(
            tenantId: apiKey.TenantId,
            action: AuditActionType.KeyRevoked,
            entity: "ApiKey",
            userId: null,
            entityId: apiKey.Id,
            details: $"Revoked API key '{apiKey.KeyPrefix}' (version {apiKey.KeyVersion}) after expiry.");
        _auditLogRepository.Add(auditLog);

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "API key {KeyId} revoked for tenant {TenantId}",
            apiKeyId, apiKey.TenantId);
    }

    /// <summary>
    /// Produces a cryptographically random plaintext key (prefix "ak_" + base64url of 32 random bytes).
    /// The plaintext is returned to the caller so it can be delivered to the key owner exactly once.
    /// </summary>
    private static string GenerateSecureKey()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var b64 = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "ak_" + b64;
    }
}
