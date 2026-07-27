namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Abstraction over an external web search provider. Implementations perform a
/// <b>real</b> HTTP call and never fake success — failure is surfaced via <see cref="SearchResult.Success"/>.
/// </summary>
public interface ISearchProvider
{
    /// <summary>Performs a web search for the given query and returns real result snippets.</summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<SearchResult> SearchAsync(string query, int maxResults, CancellationToken ct = default);
}
