using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the contract for executing a single workflow step.
/// Concrete implementations handle specific step types (e.g., agent call, code execution, user input).
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// Gets the type identifier for this executor, used to match against workflow step names.
    /// Use "*" to match any step as a fallback.
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Executes the given workflow step and returns the result.
    /// </summary>
    Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, Workflow context, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a single workflow step execution.
/// </summary>
/// <param name="IsSuccess">Whether the step execution completed successfully.</param>
/// <param name="Output">The output produced by the step, if successful.</param>
/// <param name="ErrorMessage">The error message if the step failed.</param>
/// <param name="Duration">The duration of the step execution.</param>
public record StepExecutionResult(bool IsSuccess, string? Output, string? ErrorMessage, TimeSpan Duration = default);
