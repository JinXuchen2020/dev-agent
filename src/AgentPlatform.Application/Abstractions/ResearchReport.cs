using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Abstractions;

/// <summary>A cited source used in a research report.</summary>
/// <param name="Title">The source title.</param>
/// <param name="Url">The source URL.</param>
/// <param name="Snippet">The source snippet / excerpt.</param>
public sealed record ResearchSource(string Title, string Url, string Snippet);

/// <summary>A titled section of the synthesized report body (Markdown).</summary>
/// <param name="Heading">The section heading (from a <c>## </c> Markdown heading).</param>
/// <param name="Body">The Markdown body of the section.</param>
public sealed record ResearchSection(string Heading, string Body);

/// <summary>The structured report produced by the Research Agent.</summary>
/// <param name="Question">The original research question.</param>
/// <param name="SearchQueries">The search queries that were executed.</param>
/// <param name="Sources">Deduplicated cited sources gathered from real search results.</param>
/// <param name="Answer">The synthesized answer / introduction (Markdown).</param>
/// <param name="Sections">The structured report sections (Markdown).</param>
/// <param name="StepsUsed">The number of search steps executed.</param>
/// <param name="TokenUsage">Approximate token usage across model calls.</param>
/// <param name="GeneratedAt">When the report was produced (UTC).</param>
public sealed record ResearchReport(
    string Question,
    IReadOnlyList<string> SearchQueries,
    IReadOnlyList<ResearchSource> Sources,
    string Answer,
    IReadOnlyList<ResearchSection> Sections,
    int StepsUsed,
    TokenUsage? TokenUsage,
    DateTime GeneratedAt);
