using System.Security.Claims;
using System.Text.Encodings.Web;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Auth;

/// <summary>
/// API-Key authentication handler that validates requests via the configured header
/// (default: X-API-Key). Keys are stored encrypted in the database and decrypted
/// at authentication time via <see cref="IApiKeyEncryptionService"/>.
/// Per-key roles are sourced from the database entity — no hardcoded role assignment.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly SecuritySettings _settings;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptions<SecuritySettings> settings,
        IServiceScopeFactory scopeFactory)
        : base(options, loggerFactory, encoder)
    {
        _settings = settings.Value;
        _logger = loggerFactory.CreateLogger<ApiKeyAuthenticationHandler>();
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(_settings.ApiKeyHeaderName, out var apiKeyValues))
            return AuthenticateResult.NoResult();

        var providedKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
            return AuthenticateResult.NoResult();

        using var scope = _scopeFactory.CreateScope();
        var keyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IApiKeyEncryptionService>();

        // Retrieve all active keys from DB (decrypt at auth time)
        var activeKeys = await keyRepository.GetAllActiveKeysAsync(Context.RequestAborted);

        Domain.Aggregates.ApiKeys.ApiKey? matchedKey = null;

        foreach (var storedKey in activeKeys)
        {
            try
            {
                var decrypted = encryptionService.DecryptKey(storedKey.EncryptedKeyHash);
                if (decrypted == providedKey)
                {
                    matchedKey = storedKey;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt API key {KeyPrefix} (ID: {KeyId})",
                    storedKey.KeyPrefix, storedKey.Id);
            }
        }

        if (matchedKey is null)
        {
            _logger.LogWarning("Invalid API key provided.");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var roles = matchedKey.GetRoles();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, matchedKey.TenantId.ToString()),
            new("tenant_id", matchedKey.TenantId.ToString()),
            new("key_id", matchedKey.Id.ToString()),
            new("key_version", matchedKey.KeyVersion.ToString()),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // 审计密钥使用：认证成功后记录一条 KeyUsed 审计（失败不影响认证结果）。
        try
        {
            var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var auditLog = AuditLog.Record(
                tenantId: matchedKey.TenantId,
                action: AuditActionType.KeyUsed,
                entity: "ApiKey",
                userId: null,
                entityId: matchedKey.Id,
                details: $"API key '{matchedKey.KeyPrefix}' used for authentication.");
            auditLogRepository.Add(auditLog);
            await unitOfWork.SaveChangesAsync(Context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record key-usage audit for key {KeyId}", matchedKey.Id);
        }

        return AuthenticateResult.Success(ticket);
    }
}
