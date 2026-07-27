using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using MediatR;

namespace AgentPlatform.Application.Research;

/// <summary>
/// Runs a multi-step web research for an open question: plan search queries → real web search
/// → accumulate findings (budget-compressed) → synthesize a structured report. Emits a stream
/// of <see cref="ResearchProgressEvent"/> for SSE progress reporting.
/// </summary>
/// <param name="Question">The open-ended research question.</param>
/// <param name="MaxSteps">Maximum number of search queries to execute (default 3, clamped 1–8).</param>
/// <param name="ModelId">Optional model id override; defaults to <c>StateMachineSettings.DefaultModelId</c>.</param>
/// <param name="FocusInstructions">Optional extra guidance for the synthesizer.</param>
public sealed record ResearchCommand(
    string Question,
    int? MaxSteps = null,
    string? ModelId = null,
    string? FocusInstructions = null)
    : IRequest<IAsyncEnumerable<ResearchProgressEvent>>;
