using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>Request body for <c>POST /api/v1/research</c>.</summary>
/// <param name="Question">The open-ended research question.</param>
/// <param name="MaxSteps">Maximum number of search queries to run (default 3).</param>
/// <param name="ModelId">Optional model id override.</param>
/// <param name="FocusInstructions">Optional extra guidance for the report synthesizer.</param>
public sealed record ResearchRequest(
    [Required(ErrorMessage = "问题不能为空")]
    string Question,
    int? MaxSteps = null,
    string? ModelId = null,
    string? FocusInstructions = null);
