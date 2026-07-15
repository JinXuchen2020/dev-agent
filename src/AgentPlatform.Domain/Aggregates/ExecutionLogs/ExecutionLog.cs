using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.ExecutionLogs;

/// <summary>
/// Represents an execution log aggregate root that records the history of a single workflow execution.
/// Contains a collection of <see cref="ExecutionLogEntry"/> items, one per workflow step.
/// </summary>
public sealed class ExecutionLog : IAggregateRoot
{
    private readonly List<ExecutionLogEntry> _entries = [];

    /// <summary>
    /// Gets the unique identifier of the execution log.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the identifier of the workflow this log belongs to.
    /// </summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>
    /// Gets the name of the workflow.
    /// </summary>
    public string WorkflowName { get; private init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets the tenant that owns this execution log.
    /// </summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the overall status of the workflow execution.
    /// </summary>
    public WorkflowState Status { get; private set; }

    /// <summary>
    /// Gets the total number of steps in the workflow.
    /// </summary>
    public int TotalSteps { get; private init; }

    /// <summary>
    /// Gets a read-only list of step execution entries.
    /// </summary>
    public IReadOnlyList<ExecutionLogEntry> Entries => _entries;

    /// <summary>
    /// Gets the UTC timestamp when the workflow started.
    /// </summary>
    public DateTime StartedAt { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the workflow completed or was rolled back.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    private ExecutionLog() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionLog"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the log.</param>
    /// <param name="workflowId">The identifier of the workflow.</param>
    /// <param name="workflowName">The name of the workflow.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="totalSteps">The total number of steps.</param>
    public ExecutionLog(
        Guid id,
        Guid workflowId,
        string workflowName,
        Guid tenantId,
        int totalSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        Id = id;
        WorkflowId = workflowId;
        WorkflowName = workflowName;
        TenantId = tenantId;
        TotalSteps = totalSteps;
        Status = WorkflowState.Running;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a step execution entry to the log.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    public void AddEntry(ExecutionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    /// <summary>
    /// Marks the workflow execution as completed.
    /// </summary>
    public void Complete()
    {
        Status = WorkflowState.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the workflow execution as rolled back.
    /// </summary>
    public void Rollback()
    {
        Status = WorkflowState.RolledBack;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the workflow execution as failed.
    /// </summary>
    public void Fail()
    {
        Status = WorkflowState.Failed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate. Currently unused as
    /// ExecutionLog does not raise events — retained for IAggregateRoot contract compliance.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <summary>
    /// Clears all pending domain events. No-op as ExecutionLog does not raise events.
    /// </summary>
    public void ClearDomainEvents() { }
}
