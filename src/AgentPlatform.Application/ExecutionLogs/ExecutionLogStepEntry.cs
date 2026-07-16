using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.ExecutionLogs;

/// <summary>
/// A single step entry within an execution log, used for API responses.
/// </summary>
/// <param name="Id">The entry identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="StepOrder">The zero-based step order.</param>
/// <param name="Status">The step execution status.</param>
/// <param name="Duration">The duration of the step.</param>
/// <param name="Result">The result produced by the step.</param>
/// <param name="ErrorDetail">The error detail if the step failed.</param>
/// <param name="StartedAt">When the step started.</param>
/// <param name="CompletedAt">When the step completed.</param>
public sealed record ExecutionLogStepEntry(
    Guid Id,
    string StepName,
    int StepOrder,
    WorkflowState Status,
    TimeSpan Duration,
    string? Result,
    string? ErrorDetail,
    DateTime StartedAt,
    DateTime CompletedAt);
