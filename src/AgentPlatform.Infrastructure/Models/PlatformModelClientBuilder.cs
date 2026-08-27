using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using AgentPlatform.Domain.Aggregates.PlatformModels;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Builds the platform-level <see cref="SemanticKernelModelClient"/> from the DB-backed
/// <c>PlatformModels</c> catalog (the replacement for the removed <c>RouterSettings.Candidates</c>).
/// Each enabled model is registered against its own (decrypted) key and base URL; a model whose
/// <c>EncryptedApiKey</c> is null falls back to the global <c>OpenAI:Key</c> (supports "configure only
/// a single global key" deployments). When the catalog is empty, it falls back to the legacy
/// OpenAI:* configuration path so the client still registers at least one service.
/// </summary>
internal sealed class PlatformModelClientBuilder
{
    private readonly AppDbContext _db;
    private readonly IApiKeyEncryptionService _encryption;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SemanticKernelModelClient> _logger;

    public PlatformModelClientBuilder(
        AppDbContext db,
        IApiKeyEncryptionService encryption,
        IConfiguration configuration,
        ILogger<SemanticKernelModelClient> logger)
    {
        _db = db;
        _encryption = encryption;
        _configuration = configuration;
        _logger = logger;
    }

    public SemanticKernelModelClient Build()
    {
        var models = _db.PlatformModels
            .IgnoreQueryFilters()
            .Where(m => m.IsEnabled)
            .OrderByDescending(m => m.Priority)
            .ToList();

        // Empty catalog → legacy OpenAI:* fallback (matches the env-based SemanticKernelModelClient ctor).
        if (models.Count == 0)
            return new SemanticKernelModelClient(_configuration, _logger);

        var services = new Dictionary<string, IChatCompletionService>(StringComparer.Ordinal);
        foreach (var m in models)
        {
            // 模型可自带密文 Key；为空则回退全局 OpenAI:Key（兼容“仅配置全局 Key”的部署）。
            var key = m.EncryptedApiKey is null
                ? _configuration["OpenAI:Key"]
                : _encryption.DecryptKey(m.EncryptedApiKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning(
                    "Skipping platform model {Model}: no API key resolved (DB row has no EncryptedApiKey and OpenAI:Key is empty).",
                    m.ModelName);
                continue;
            }

            var service = SemanticKernelModelClient.BuildService(m.ModelName, m.BaseUrl, key);
            services[m.ModelName] = service;
            services[$"{m.Provider}:{m.ModelName}"] = service;
        }

        // No usable model resolved → fall back so the client is never empty (router needs ≥1 candidate).
        if (services.Count == 0)
            return new SemanticKernelModelClient(_configuration, _logger);

        return new SemanticKernelModelClient(services);
    }
}
