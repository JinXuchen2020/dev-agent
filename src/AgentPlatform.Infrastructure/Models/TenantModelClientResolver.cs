using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Resolves the per-tenant LLM model clients by decrypting each of the tenant's enabled BYO model
/// credentials and building a <see cref="SemanticKernelModelClient"/> per credential. Returns an empty list
/// when the tenant has no active model credential, in which case the caller falls back to platform models.
/// This is the core of per-tenant model isolation, extended to support multiple BYO models per tenant.
/// </summary>
internal sealed class TenantModelClientResolver : ITenantModelClientResolver
{
    private readonly ITenantCredentialResolver _credentialResolver;
    private readonly IApiKeyEncryptionService _encryption;
    private readonly ILogger<TenantModelClientResolver> _logger;
    private readonly ILogger<ModelTelemetryDecorator> _telemetryLogger;
    private readonly IConfiguration _configuration;

    public TenantModelClientResolver(
        ITenantCredentialResolver credentialResolver,
        IApiKeyEncryptionService encryption,
        ILogger<TenantModelClientResolver> logger,
        ILogger<ModelTelemetryDecorator> telemetryLogger,
        IConfiguration configuration)
    {
        _credentialResolver = credentialResolver;
        _encryption = encryption;
        _logger = logger;
        _telemetryLogger = telemetryLogger;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<TenantModelResolution>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Stub 模式（本地/演示/集成测试）下，BYO 凭据也一律不发起真实 LLM 调用：
        // 直接回退到平台 stub 客户端，避免任何测试/演示数据触发真实网络请求导致超时（F28 集成环境契约）。
        if (string.Equals(_configuration["ModelClient:Provider"], "Stub", StringComparison.Ordinal))
            return Array.Empty<TenantModelResolution>();

        var settings = await _credentialResolver.ResolveAsync(tenantId, CredentialCategory.Model, ct);
        var enabled = settings.Where(s => s.IsEnabled).ToList();
        if (enabled.Count == 0)
            return Array.Empty<TenantModelResolution>();

        var resolutions = new List<TenantModelResolution>(enabled.Count);
        foreach (var setting in enabled)
        {
            var plaintextKey = _encryption.DecryptKey(setting.EncryptedApiKey);
            var modelName = setting.ModelName ?? "gpt-4o";
            var provider = NormalizeProvider(setting.Provider);

            var client = SemanticKernelModelClient.CreateForTenant(plaintextKey, setting.BaseUrl, modelName, provider);
            var decorated = new ModelTelemetryDecorator(client, _telemetryLogger);
            var candidates = new List<ModelCandidate> { new(modelName, provider, 100) };

            _logger.LogInformation(
                "Resolved tenant model client for tenant {TenantId} credential {CredentialId} provider {Provider} model {Model}",
                tenantId, setting.Id, provider, modelName);
            resolutions.Add(new TenantModelResolution(decorated, candidates));
        }

        return resolutions;
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "openai" => "openai",
        "deepseek" => "deepseek",
        "vllm" => "vllm",
        "custom" => "custom",
        _ => provider.Trim().ToLowerInvariant()
    };
}
