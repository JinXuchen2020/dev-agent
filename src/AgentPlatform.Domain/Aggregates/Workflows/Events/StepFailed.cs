using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workflows.Events;

/// <summary>
/// Represents a domain event raised when a workflow step fails.
/// </summary>
/// <param name="WorkflowId">The unique identifier of the workflow.</param>
/// <param name="StepId">The unique identifier of the step.</param>
/// <param name="StepName">The name of the step.</param>
/// <param name="StepOrder">The zero-based execution order of the step.</param>
/// <param name="ErrorDetail">A description of the error that occurred.</param>
public sealed record StepFailed(
    Guid WorkflowId,
    Guid StepId,
    string StepName,
    int StepOrder,
    string? ErrorDetail,
    TimeSpan Duration
) : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
