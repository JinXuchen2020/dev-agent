using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workflows.Events;

/// <summary>
/// Represents a domain event raised when a workflow completes all steps successfully.
/// </summary>
/// <param name="WorkflowId">The unique identifier of the workflow.</param>
/// <param name="Name">The name of the workflow.</param>
/// <param name="TotalSteps">The total number of steps in the workflow.</param>
/// <param name="TenantId">The identifier of the tenant that owns the workflow.</param>
public sealed record WorkflowCompleted(
    Guid WorkflowId,
    string Name,
    int TotalSteps,
    Guid TenantId
) : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
