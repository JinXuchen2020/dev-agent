namespace AgentPlatform.Application.Abstractions;

/// <summary>A single web search hit returned by a search provider.</summary>
/// <param name="Title">The page title.</param>
/// <param name="Url">The page URL.</param>
/// <param name="Snippet">The result snippet / summary text.</param>
public sealed record SearchSnippet(string Title, string Url, string Snippet);

/// <summary>The outcome of a single search call (real HTTP, never faked).</summary>
/// <param name="Success">Whether the search succeeded.</param>
/// <param name="Snippets">The retrieved snippets (empty on failure).</param>
/// <param name="ErrorMessage">The real error reason when <see cref="Success"/> is false.</param>
public sealed record SearchResult(
    bool Success,
    IReadOnlyList<SearchSnippet> Snippets,
    string? ErrorMessage = null);
