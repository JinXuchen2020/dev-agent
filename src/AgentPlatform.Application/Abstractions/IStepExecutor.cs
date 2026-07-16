using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the contract for executing a single workflow step.
/// Consumes the unified <see cref="WorkflowContext"/> (Blueprint C.3) instead of
/// the old dual-track (WorkflowStep + Workflow aggregate).
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// Gets the type identifier for this executor, used to match against workflow step names.
    /// Use "*" to match any step as a fallback.
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Executes the given workflow step using the unified context and returns the result.
    /// </summary>
    /// <param name="step">The workflow step to execute.</param>
    /// <param name="ctx">The unified workflow context (contains artifacts, blackboard, RAG retrieval, summary).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, WorkflowContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a single workflow step execution.
/// </summary>
/// <param name="Outcome">Categorizes the result (Success / FailedRetry / FailedRollback / NeedsIntervention).</param>
/// <param name="Output">The output produced by the step, if successful.</param>
/// <param name="Artifact">Structured artifact content for the WorkflowContext (JSON). Null if no artifact produced.</param>
/// <param name="ErrorMessage">The error message if the step failed.</param>
/// <param name="Duration">The duration of the step execution.</param>
public record StepExecutionResult(
    StepOutcome Outcome,
    string? Output,
    string? Artifact,
    string? ErrorMessage,
    TimeSpan Duration = default)
{
    /// <summary>
    /// Creates a successful result with optional artifact.
    /// </summary>
    public static StepExecutionResult Success(string? output = null, string? artifact = null, TimeSpan duration = default)
        => new(StepOutcome.Success, output, artifact, null, duration);

    /// <summary>
    /// Creates a retryable failure result.
    /// </summary>
    public static StepExecutionResult RetryableFailure(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.FailedRetry, null, null, errorMessage, duration);

    /// <summary>
    /// Creates an unrecoverable failure result that triggers rollback.
    /// </summary>
    public static StepExecutionResult FatalFailure(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.FailedRollback, null, null, errorMessage, duration);

    /// <summary>
    /// Creates a result requesting human intervention.
    /// </summary>
    public static StepExecutionResult NeedsIntervention(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.NeedsIntervention, null, null, errorMessage, duration);
}
