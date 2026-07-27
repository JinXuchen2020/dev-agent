using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Resolves the per-tenant LLM model client by decrypting the tenant's BYO credential and building a
/// <see cref="SemanticKernelModelClient"/> for it. Returns null when the tenant has no active model credential,
/// in which case the caller falls back to platform models. This is the core of per-tenant model isolation.
/// </summary>
internal sealed class TenantModelClientResolver : ITenantModelClientResolver
{
    private readonly ITenantCredentialResolver _credentialResolver;
    private readonly IApiKeyEncryptionService _encryption;
    private readonly ILogger<TenantModelClientResolver> _logger;
    private readonly ILogger<ModelTelemetryDecorator> _telemetryLogger;

    public TenantModelClientResolver(
        ITenantCredentialResolver credentialResolver,
        IApiKeyEncryptionService encryption,
        ILogger<TenantModelClientResolver> logger,
        ILogger<ModelTelemetryDecorator> telemetryLogger)
    {
        _credentialResolver = credentialResolver;
        _encryption = encryption;
        _logger = logger;
        _telemetryLogger = telemetryLogger;
    }

    public async Task<TenantModelResolution?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var setting = await _credentialResolver.ResolveAsync(tenantId, CredentialCategory.Model, ct);
        if (setting is null || !setting.IsEnabled)
            return null;

        var plaintextKey = _encryption.DecryptKey(setting.EncryptedApiKey);
        var modelName = setting.ModelName ?? "gpt-4o";
        var provider = NormalizeProvider(setting.Provider);

        var client = SemanticKernelModelClient.CreateForTenant(plaintextKey, setting.BaseUrl, modelName, provider);
        var decorated = new ModelTelemetryDecorator(client, _telemetryLogger);
        var candidates = new List<ModelCandidate> { new(modelName, provider, 100) };

        _logger.LogInformation(
            "Resolved tenant model client for tenant {TenantId} provider {Provider} model {Model}",
            tenantId, provider, modelName);
        return new TenantModelResolution(decorated, candidates);
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
