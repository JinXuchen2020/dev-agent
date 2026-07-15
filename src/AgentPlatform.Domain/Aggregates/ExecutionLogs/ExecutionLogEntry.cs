using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.ExecutionLogs;

/// <summary>
/// Represents a single entry in an execution log, recording the outcome of one workflow step.
/// </summary>
public sealed class ExecutionLogEntry
{
    /// <summary>
    /// Gets the unique identifier of the log entry.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the name of the step that was executed.
    /// </summary>
    public string StepName { get; private init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets the zero-based execution order of the step.
    /// </summary>
    public int StepOrder { get; private init; }

    /// <summary>
    /// Gets the execution state of the step (e.g., Completed, Failed).
    /// </summary>
    public WorkflowState Status { get; private set; }

    /// <summary>
    /// Gets the duration of the step execution.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Gets the result produced by the step, if any.
    /// </summary>
    public string? Result { get; private set; }

    /// <summary>
    /// Gets the error detail if the step failed, otherwise <c>null</c>.
    /// </summary>
    public string? ErrorDetail { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the step execution started.
    /// </summary>
    public DateTime StartedAt { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the step execution completed.
    /// </summary>
    public DateTime CompletedAt { get; private set; }

    private ExecutionLogEntry() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionLogEntry"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the entry.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="stepOrder">The zero-based execution order.</param>
    /// <param name="status">The execution status of the step.</param>
    /// <param name="duration">The duration of the step execution.</param>
    /// <param name="result">The result produced by the step, if any.</param>
    /// <param name="errorDetail">The error detail if the step failed, otherwise <c>null</c>.</param>
    public ExecutionLogEntry(
        Guid id,
        string stepName,
        int stepOrder,
        WorkflowState status,
        TimeSpan duration,
        string? result,
        string? errorDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);

        Id = id;
        StepName = stepName;
        StepOrder = stepOrder;
        Status = status;
        Duration = duration;
        Result = result;
        ErrorDetail = errorDetail;
        StartedAt = DateTime.UtcNow;
        CompletedAt = StartedAt + duration;
    }
}
