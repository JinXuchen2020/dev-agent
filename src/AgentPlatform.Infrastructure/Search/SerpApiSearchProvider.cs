using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Search;

/// <summary>
/// Real HTTP implementation of <see cref="ISearchProvider"/> against SerpApi.
/// Performs a genuine GET to the search endpoint and parses <c>organic_results</c>.
/// Never fakes success: missing key / non-2xx / timeout / transport error are surfaced as
/// <see cref="SearchResult.Success"/> = false with the real reason.
/// </summary>
internal sealed class SerpApiSearchProvider : ISearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearchSettings _settings;
    private readonly ILogger<SerpApiSearchProvider> _logger;

    public SerpApiSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<SearchSettings> settings,
        ILogger<SerpApiSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchResult> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SerpApiKey))
        {
            return new SearchResult(false, Array.Empty<SearchSnippet>(),
                "搜索 API 密钥未配置（Search:SerpApiKey 或环境变量 Search__SerpApiKey）");
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            var client = _httpClientFactory.CreateClient(nameof(SerpApiSearchProvider));
            var url = $"{_settings.BaseUrl}?engine=google&q={Uri.EscapeDataString(query)}" +
                      $"&num={maxResults}&api_key={Uri.EscapeDataString(_settings.SerpApiKey)}";

            var response = await client.GetAsync(url, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new SearchResult(false, Array.Empty<SearchSnippet>(),
                    $"搜索 API 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync(linked.Token);
            var snippets = ParseOrganicResults(json);
            _logger.LogInformation("SerpApi 检索 q={Query} 命中 {Count} 条", query, snippets.Count);
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
            _logger.LogError(ex, "SerpApi 检索异常 q={Query}", query);
            return new SearchResult(false, Array.Empty<SearchSnippet>(), ex.Message);
        }
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
