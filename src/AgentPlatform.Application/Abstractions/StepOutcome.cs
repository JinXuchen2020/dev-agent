namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Categorizes the result of a single workflow step execution.
/// Used by the orchestration primitive to decide the next action
/// (proceed, retry, rollback, or end).
/// </summary>
public enum StepOutcome
{
    /// <summary>The step completed successfully.</summary>
    Success,

    /// <summary>The step failed and should be retried (up to max retry attempts).</summary>
    FailedRetry,

    /// <summary>The step failed with an unrecoverable error; the workflow should roll back.</summary>
    FailedRollback,

    /// <summary>The step requires human intervention (HITL breakpoint).</summary>
    NeedsIntervention
}
