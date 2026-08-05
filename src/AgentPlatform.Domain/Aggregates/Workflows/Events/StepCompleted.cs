using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workflows.Events;

/// <summary>
/// Represents a domain event raised when a single workflow step completes successfully.
/// </summary>
/// <param name="WorkflowId">The unique identifier of the workflow.</param>
/// <param name="StepId">The unique identifier of the step.</param>
/// <param name="StepName">The name of the step.</param>
/// <param name="StepOrder">The zero-based execution order of the step.</param>
/// <param name="Result">The result produced by the step, if any.</param>
/// <param name="NodeType">The node/step type (F24 trace), if known.</param>
/// <param name="TokenUsage">Token usage of the step (F24 trace), if reported.</param>
public sealed record StepCompleted(
    Guid WorkflowId,
    Guid StepId,
    string StepName,
    int StepOrder,
    string? Result,
    TimeSpan Duration,
    Domain.Enums.StepType? NodeType = null,
    Domain.ValueObjects.TokenUsage? TokenUsage = null
) : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
