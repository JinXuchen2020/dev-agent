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
/// <param name="NodeType">The node/step type (F24 trace), if known.</param>
/// <param name="TokenUsage">Token usage of the step (F24 trace), if reported.</param>
public sealed record StepFailed(
    Guid WorkflowId,
    Guid StepId,
    string StepName,
    int StepOrder,
    string? ErrorDetail,
    TimeSpan Duration,
    Domain.Enums.StepType? NodeType = null,
    Domain.ValueObjects.TokenUsage? TokenUsage = null
) : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
