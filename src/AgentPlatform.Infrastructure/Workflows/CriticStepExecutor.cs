using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a critic/review step within the negotiation preset (Blueprint C.6).
/// Reviews the previous step's artifact and returns a structured diff with
/// either approval or rework instructions.
///
/// This enables range-specific rework (targeted fixes) instead of full pipeline restart.
/// </summary>
internal sealed class CriticStepExecutor : IStepExecutor
{
    private readonly ILogger<CriticStepExecutor> _logger;

    public string StepType => "*critic*";

    public CriticStepExecutor(ILogger<CriticStepExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
    {
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

            // Simulate review — always approve for now
            // In production, the critic agent would call IModelClient with a review prompt
            await Task.Delay(50, ct);

            var reviewResult = new CriticReviewResult
            {
                Approved = true,
                StepName = lastArtifact.StepName,
                Feedback = "Artifact meets quality standards.",
                ReworkTarget = null,
                Diff = null
            };

            var artifactJson = JsonSerializer.Serialize(reviewResult);

            // Write convergence signal to blackboard via the context's blackboard
            var updatedBb = ctx.Blackboard.Set("negotiation:converged", reviewResult.Approved ? "true" : "false");

            _logger.LogInformation("Critic review for {TargetStep}: {Verdict}",
                lastArtifact.StepName, reviewResult.Approved ? "APPROVED" : "REWORK_REQUIRED");

            return StepExecutionResult.Success(
                reviewResult.Approved ? "APPROVED" : "REWORK_REQUIRED",
                artifactJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critic step {StepName} failed", step.StepName);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }
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
