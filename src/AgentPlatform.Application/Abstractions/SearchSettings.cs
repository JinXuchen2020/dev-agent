namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration for the web search provider used by the Research Agent.
/// The API key must come from configuration / environment variable (never committed, never stored in DB).
/// </summary>
public sealed class SearchSettings
{
    /// <summary>Provider name (e.g. "SerpApi"). Reserved for future multi-provider support.</summary>
    public string Provider { get; set; } = "SerpApi";

    /// <summary>
    /// SerpApi API key. Leave empty in source; supply via environment variable
    /// <c>Search__SerpApiKey</c> in production. Never persisted to the database.
    /// </summary>
    public string SerpApiKey { get; set; } = string.Empty;

    /// <summary>Base URL of the search API. Defaults to the SerpApi endpoint.</summary>
    public string BaseUrl { get; set; } = "https://serpapi.com/search.json";

    /// <summary>HTTP timeout in seconds for a single search call.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Default number of results requested per query.</summary>
    public int DefaultMaxResults { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of platform-provided search calls a single tenant may make per day.
    /// BYO-SerpApi (tenant-owned) search is not subject to this quota. Default 100 calls/tenant/day (F13 S2).
    /// </summary>
    public int PerTenantDailySearchQuota { get; set; } = 100;
}
