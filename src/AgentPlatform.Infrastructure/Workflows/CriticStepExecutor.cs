using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a critic/review step within the negotiation preset (Blueprint C.6).
/// Reviews the previous step's artifact via IModelClient and returns a structured
/// diff with either approval or rework instructions.
///
/// This enables range-specific rework (targeted fixes) instead of full pipeline restart.
/// Uses IModelClient for real review — fallback to "always approve" only if model is unavailable.
/// </summary>
internal sealed class CriticStepExecutor : IStepExecutor
{
    private readonly ILogger<CriticStepExecutor> _logger;
    private readonly IModelClient _modelClient;
    private readonly StateMachineSettings _settings;

    public string StepType => "*critic*";

    public CriticStepExecutor(
        ILogger<CriticStepExecutor> logger,
        IModelClient modelClient,
        IOptions<StateMachineSettings> settings)
    {
        _logger = logger;
        _modelClient = modelClient;
        _settings = settings.Value;
    }

    public async Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        _logger.LogInformation("Critic reviewing step {StepName} for workflow {WorkflowId}",
            step.StepName, ctx.WorkflowId);

        try
        {
            // Find the most recent completed artifact to review
            var lastArtifact = ctx.Artifacts.Values
                .OrderByDescending(a => a.ProducedAt)
                .FirstOrDefault();

            if (lastArtifact == null)
            {
                _logger.LogWarning("No artifacts to review for critic step {StepName}", step.StepName);
                return StepExecutionResult.Success("No artifacts to review");
            }

            // Build a review prompt for the critic model
            var systemPrompt = "You are a quality reviewer on a software development team. " +
                "Analyze the following artifact produced by a team member and determine if it meets quality standards. " +
                "Respond with a JSON object containing:\n" +
                "- \"Approved\": true if the artifact is acceptable, false if it needs rework\n" +
                "- \"Feedback\": specific, actionable feedback about the artifact\n" +
                "- \"ReworkTarget\": the step that should be reworked (or null if approved)\n" +
                "- \"Diff\": specific issues found (or null if approved)";

            var userPrompt = $"Review this {lastArtifact.ContentType} artifact from step '{lastArtifact.StepName}':\n\n" +
                Truncate(lastArtifact.Content, 4000);

            var messages = new List<ChatMessage>
            {
                new(MessageRole.System, systemPrompt),
                new(MessageRole.User, userPrompt)
            };

            CriticReviewResult reviewResult;

            try
            {
                var modelId = _settings.DefaultModelId;
                var response = await _modelClient.ChatAsync(modelId, messages, ct);
                reviewResult = ParseReviewResult(response.Content, lastArtifact.StepName, _settings.AllowCriticOverride);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_settings.AllowCriticOverride)
                {
                    // AllowOverride=true → silently approve (legacy behavior)
                    _logger.LogWarning(ex,
                        "Critic model unavailable for step {StepName}, AllowCriticOverride=true — approving",
                        step.StepName);
                    reviewResult = new CriticReviewResult
                    {
                        Approved = true,
                        StepName = lastArtifact.StepName,
                        Feedback = "Auto-approved (critic model unavailable, AllowCriticOverride=true).",
                        ReworkTarget = null,
                        Diff = null
                    };
                }
                else
                {
                    // AllowOverride=false (default) → fail-loud: produce a rejection so the
                    // CriticConvergenceTermination keeps the negotiation loop running
                    _logger.LogError(ex,
                        "Critic model threw for step {StepName}, AllowCriticOverride=false — rejecting (fail-loud)",
                        step.StepName);
                    reviewResult = new CriticReviewResult
                    {
                        Approved = false,
                        StepName = lastArtifact.StepName,
                        Feedback = $"Rejected (critic model error): {ex.Message}",
                        ReworkTarget = lastArtifact.StepName,
                        Diff = $"Critic model threw: {ex.Message}"
                    };
                }
            }

            var artifactJson = JsonSerializer.Serialize(reviewResult);

            _logger.LogInformation("Critic review for {TargetStep}: {Verdict}",
                lastArtifact.StepName, reviewResult.Approved ? "APPROVED" : "REWORK_REQUIRED");

            // Return artifact JSON as both output AND artifact so CriticConvergenceTermination
            // can scan artifacts for approval signal (Blackboard is per-request, not persisted)
            return StepExecutionResult.Success(artifactJson, artifactJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critic step {StepName} failed", step.StepName);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private static CriticReviewResult ParseReviewResult(string content, string fallbackStepName, bool allowOverride)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CriticReviewResult>(content);
            if (result != null)
            {
                // Accept valid JSON; use fallback StepName when the response did not include one.
                if (string.IsNullOrEmpty(result.StepName))
                    result = result with { StepName = fallbackStepName };
                return result;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through to default handling
        }

        if (allowOverride)
        {
            // AllowOverride=true → silently approve on unparseable response (legacy behavior)
            return new CriticReviewResult
            {
                Approved = true,
                StepName = fallbackStepName,
                Feedback = content.Length > 500 ? content[..500] : content,
                ReworkTarget = null,
                Diff = null
            };
        }

        // AllowOverride=false (default) → reject: unparseable critic response counts as rejection
        return new CriticReviewResult
        {
            Approved = false,
            StepName = fallbackStepName,
            Feedback = $"Invalid critic response (unparseable JSON): {content[..Math.Min(content.Length, 200)]}",
            ReworkTarget = fallbackStepName,
            Diff = "Critic response was not valid JSON"
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Structured output from a critic/review step.
/// Enables range-specific rework: the critic specifies which step to rework and what to fix.
/// </summary>
internal sealed record CriticReviewResult
{
    /// <summary>Whether the reviewed artifact is approved.</summary>
    public bool Approved { get; init; }

    /// <summary>The name of the step that was reviewed.</summary>
    public string StepName { get; init; } = "";

    /// <summary>Human-readable feedback.</summary>
    public string Feedback { get; init; } = "";

    /// <summary>
    /// If not approved, the step that should be reworked (range-specific).
    /// Null means the most recent non-critic step.
    /// </summary>
    public string? ReworkTarget { get; init; }

    /// <summary>Structured diff or specific issues found.</summary>
    public string? Diff { get; init; }
}
