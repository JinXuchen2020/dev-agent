using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Search;

/// <summary>
/// Real HTTP implementation of <see cref="ISearchProvider"/> against SerpApi.
/// Performs a genuine GET to the search endpoint and parses <c>organic_results</c>.
/// Never fakes success: missing key / non-2xx / timeout / transport error are surfaced as
/// <see cref="SearchResult.Success"/> = false with the real reason.
///
/// Tenant isolation (F13): the API key is resolved per request from the tenant's BYO SerpApi credential
/// when configured; otherwise it falls back to the operator-configured platform SerpApi key. BYO keys bypass
/// the per-tenant search quota; platform searches are subject to <c>PerTenantDailySearchQuota</c>.
/// </summary>
internal sealed class SerpApiSearchProvider : ISearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearchSettings _settings;
    private readonly ITenantCredentialResolver _credentialResolver;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICostController _costController;
    private readonly IApiKeyEncryptionService _encryption;
    private readonly ILogger<SerpApiSearchProvider> _logger;

    public SerpApiSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<SearchSettings> settings,
        ITenantCredentialResolver credentialResolver,
        ITenantProvider tenantProvider,
        ICostController costController,
        IApiKeyEncryptionService encryption,
        ILogger<SerpApiSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _credentialResolver = credentialResolver;
        _tenantProvider = tenantProvider;
        _costController = costController;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<SearchResult> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetTenantId();

        string apiKey;
        var byoKey = await TryResolveTenantKeyAsync(tenantId, ct);
        if (byoKey is not null)
        {
            // Tenant-owned key: isolated, no platform quota.
            apiKey = byoKey;
        }
        else
        {
            // Platform built-in search (B): operator-configured key, subject to per-tenant quota.
            if (string.IsNullOrWhiteSpace(_settings.SerpApiKey))
            {
                return new SearchResult(false, Array.Empty<SearchSnippet>(),
                    "搜索 API 密钥未配置（请在设置中填写 SerpApi Key 或联系运营方）");
            }

            if (!_costController.TryRecordSearch(tenantId))
            {
                return new SearchResult(false, Array.Empty<SearchSnippet>(),
                    "平台搜索今日配额已用尽（PerTenantDailySearchQuota），请配置 BYO-SerpApi Key");
            }

            apiKey = _settings.SerpApiKey;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            var client = _httpClientFactory.CreateClient(nameof(SerpApiSearchProvider));
            var url = $"{_settings.BaseUrl}?engine=google&q={Uri.EscapeDataString(query)}" +
                      $"&num={maxResults}&api_key={Uri.EscapeDataString(apiKey)}";

            var response = await client.GetAsync(url, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new SearchResult(false, Array.Empty<SearchSnippet>(),
                    $"搜索 API 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync(linked.Token);
            var snippets = ParseOrganicResults(json);
            _logger.LogInformation("SerpApi 检索 q={Query} 命中 {Count} 条 tenant={TenantId}", query, snippets.Count, tenantId);
            return new SearchResult(true, snippets.ToArray());
        }
        catch (OperationCanceledException)
        {
            return new SearchResult(false, Array.Empty<SearchSnippet>(), "搜索超时");
        }
        catch (HttpRequestException ex)
        {
            return new SearchResult(false, Array.Empty<SearchSnippet>(), $"搜索请求失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SerpApi 检索异常 q={Query} tenant={TenantId}", query, tenantId);
            return new SearchResult(false, Array.Empty<SearchSnippet>(), ex.Message);
        }
    }

    private async Task<string?> TryResolveTenantKeyAsync(Guid tenantId, CancellationToken ct)
    {
        var creds = await _credentialResolver.ResolveAsync(tenantId, CredentialCategory.Search, ct);
        var cred = creds.FirstOrDefault(c => c.IsEnabled);
        if (cred is null)
            return null;

        return _encryption.DecryptKey(cred.EncryptedApiKey);
    }

    private static List<SearchSnippet> ParseOrganicResults(string json)
    {
        var snippets = new List<SearchSnippet>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("organic_results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return snippets;
        }

        foreach (var item in results.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var link = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
            var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(link))
                continue;
            snippets.Add(new SearchSnippet(title, link, snippet));
        }

        return snippets;
    }
}
