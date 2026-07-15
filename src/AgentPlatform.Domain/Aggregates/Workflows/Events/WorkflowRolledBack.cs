using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workflows.Events;

/// <summary>
/// Represents a domain event raised when a workflow is rolled back due to an unrecoverable step failure.
/// </summary>
/// <param name="WorkflowId">The unique identifier of the workflow.</param>
/// <param name="Name">The name of the workflow.</param>
/// <param name="FailedStepName">The name of the step whose failure triggered the rollback.</param>
/// <param name="ErrorDetail">A description of the error that caused the rollback.</param>
/// <param name="TenantId">The identifier of the tenant that owns the workflow.</param>
public sealed record WorkflowRolledBack(
    Guid WorkflowId,
    string Name,
    string FailedStepName,
    string? ErrorDetail,
    Guid TenantId
) : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
