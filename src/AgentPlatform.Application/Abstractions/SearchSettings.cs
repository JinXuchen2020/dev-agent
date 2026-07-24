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
}
