using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the contract for executing a single workflow step/node.
/// Consumes the unified <see cref="IWorkflowExecutable"/> (Blueprint C.3) instead of
/// the old dual-track (WorkflowStep + Workflow aggregate).
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// Legacy glob pattern used to match against a step name (e.g. "*" fallback,
    /// "*critic*"). Retained for backward compatibility with linear steps.
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Explicit step type for DAG routing. Null for legacy linear steps that rely on
    /// the <see cref="StepType"/> glob match.
    /// </summary>
    StepType? HandlesType { get; }

    /// <summary>
    /// Executes the given workflow step/node using the unified context and returns the result.
    /// </summary>
    /// <param name="step">The workflow step/node to execute.</param>
    /// <param name="ctx">The unified workflow context (contains artifacts, blackboard, RAG retrieval, summary).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a single workflow step execution.
/// </summary>
public record StepExecutionResult(
    StepOutcome Outcome,
    string? Output,
    string? Artifact,
    string? ErrorMessage,
    TimeSpan Duration = default)
{
    /// <summary>Creates a successful result with optional artifact.</summary>
    public static StepExecutionResult Success(string? output = null, string? artifact = null, TimeSpan duration = default)
        => new(StepOutcome.Success, output, artifact, null, duration);

    /// <summary>Creates a retryable failure result.</summary>
    public static StepExecutionResult RetryableFailure(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.FailedRetry, null, null, errorMessage, duration);

    /// <summary>Creates an unrecoverable failure result that triggers rollback.</summary>
    public static StepExecutionResult FatalFailure(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.FailedRollback, null, null, errorMessage, duration);

    /// <summary>Creates a result requesting human intervention.</summary>
    public static StepExecutionResult NeedsIntervention(string errorMessage, TimeSpan duration = default)
        => new(StepOutcome.NeedsIntervention, null, null, errorMessage, duration);
}
