using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Abstractions;

/// <summary>Discriminates the kind of progress event emitted while a research runs.</summary>
public enum ResearchEventType
{
    /// <summary>The planner produced the search query list.</summary>
    Plan = 0,
    /// <summary>A search for a specific query has started.</summary>
    SearchStart = 1,
    /// <summary>A search completed (success or failure).</summary>
    SearchDone = 2,
    /// <summary>The synthesizer is composing the final report.</summary>
    Synthesize = 3,
    /// <summary>The final report is ready.</summary>
    Report = 4,
    /// <summary>The research failed; <see cref="ResearchProgressEvent.Error"/> carries the reason.</summary>
    Error = 5
}

/// <summary>A single progress event streamed over SSE during a research run.</summary>
/// <param name="Type">The event kind.</param>
/// <param name="Message">A human-readable status message.</param>
/// <param name="Queries">The planned query list (on <see cref="ResearchEventType.Plan"/>).</param>
/// <param name="Query">The query being searched (on SearchStart / SearchDone).</param>
/// <param name="SnippetCount">Number of snippets returned (on SearchDone).</param>
/// <param name="Report">The final report (on <see cref="ResearchEventType.Report"/>).</param>
/// <param name="Error">The error reason (on <see cref="ResearchEventType.Error"/>).</param>
public sealed record ResearchProgressEvent(
    ResearchEventType Type,
    string? Message = null,
    IReadOnlyList<string>? Queries = null,
    string? Query = null,
    int? SnippetCount = null,
    ResearchReport? Report = null,
    string? Error = null);
