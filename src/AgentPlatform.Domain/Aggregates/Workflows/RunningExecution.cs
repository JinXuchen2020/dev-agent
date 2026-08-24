using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// Represents an in-flight workflow execution for durable scheduling.
/// Replaces the in-memory <c>ConcurrentDictionary<Guid, RunningCtsEntry></c> with a DB-backed truth source
/// that survives process restarts and enables multi-instance lease coordination.
/// </summary>
public sealed class RunningExecution : IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Gets the unique identifier of the running execution (equals WorkflowId for 1:1 mapping).</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the workflow identifier this execution belongs to.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Gets the tenant identifier that owns this execution.</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the current workflow state (Running / Paused).</summary>
    public WorkflowState WorkflowState { get; private set; }

    /// <summary>
    /// Updates the workflow state (internal use for state transitions).
    /// </summary>
    public void SetWorkflowState(WorkflowState state)
    {
        WorkflowState = state;
    }

    /// <summary>Gets the UTC timestamp of the last heartbeat from the orchestration process.</summary>
    public DateTime HeartbeatAt { get; private set; }

    /// <summary>Gets the UTC timestamp when the current lease expires. Expired lease = eligible for takeover by scheduler.</summary>
    public DateTime LeaseExpiresAt { get; private set; }

    /// <summary>Gets the unique identifier of the process instance currently holding the lease.</summary>
    public string InstanceId { get; private set; } = null!;

    /// <summary>Gets the checkpoint version this execution has processed (mirrors <see cref="ExecutionLog.CheckpointVersion"/>).</summary>
    public int CheckpointVersion { get; private set; }

    /// <summary>Gets the optional serialized Blackboard snapshot for quick resume without loading full ExecutionLog.</summary>
    public string? BlackboardSnapshot { get; private set; }

    /// <summary>Gets the collection of domain events raised by this aggregate.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private RunningExecution() { }

    /// <summary>
    /// Creates a new running execution record for a workflow starting execution.
    /// </summary>
    public static RunningExecution Create(Guid workflowId, Guid tenantId, string instanceId, TimeSpan leaseTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (leaseTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Lease TTL must be positive.");

        var now = DateTime.UtcNow;
        return new RunningExecution
        {
            Id = workflowId, // 1:1 with WorkflowId
            WorkflowId = workflowId,
            TenantId = tenantId,
            WorkflowState = WorkflowState.Running,
            HeartbeatAt = now,
            LeaseExpiresAt = now.Add(leaseTtl),
            InstanceId = instanceId,
            CheckpointVersion = 0,
            BlackboardSnapshot = null
        };
    }

    /// <summary>
    /// Attempts to acquire the lease for this instance. Returns true if acquired (either unleased or lease expired).
    /// </summary>
    public bool TryAcquireLease(string instanceId, TimeSpan leaseTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (leaseTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Lease TTL must be positive.");

        var now = DateTime.UtcNow;
        if (WorkflowState != WorkflowState.Running)
            return false; // Only Running executions can be leased

        if (string.IsNullOrEmpty(InstanceId) || now >= LeaseExpiresAt || string.Equals(InstanceId, this.InstanceId, StringComparison.Ordinal))
        {
            InstanceId = instanceId;
            LeaseExpiresAt = now.Add(leaseTtl);
            HeartbeatAt = now;
            return true;
        }

        return false; // Lease held by another active instance
    }

    /// <summary>
    /// Renews the lease for the current instance. Returns true if renewal succeeded (caller still holds lease).
    /// </summary>
    public bool TryRenewLease(string instanceId, TimeSpan leaseTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (leaseTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Lease TTL must be positive.");

        if (!string.Equals(InstanceId, instanceId, StringComparison.Ordinal))
            return false; // Lease lost to another instance

        var now = DateTime.UtcNow;
        if (now >= LeaseExpiresAt)
            return false; // Lease already expired (should not happen if caller heartbeats correctly)

        LeaseExpiresAt = now.Add(leaseTtl);
        HeartbeatAt = now;
        return true;
    }

    /// <summary>
    /// Releases the lease if held by the given instance. Returns true if released.
    /// </summary>
    public bool ReleaseLease(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!string.Equals(InstanceId, instanceId, StringComparison.Ordinal))
            return false; // Not our lease

        InstanceId = string.Empty;
        LeaseExpiresAt = DateTime.MinValue;
        return true;
    }

    /// <summary>
    /// Updates the heartbeat with the latest checkpoint version and optional Blackboard snapshot.
    /// Caller must hold the lease (enforced by scheduler/orchestrator).
    /// </summary>
    public void UpdateHeartbeat(int checkpointVersion, string? blackboardSnapshot)
    {
        if (checkpointVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpointVersion));

        HeartbeatAt = DateTime.UtcNow;
        CheckpointVersion = checkpointVersion;
        BlackboardSnapshot = blackboardSnapshot;
    }

    /// <summary>
    /// Marks the execution as paused (e.g., on explicit PauseAsync or NeedsIntervention).
    /// </summary>
    public void Pause()
    {
        WorkflowState = WorkflowState.Paused;
        // Lease is implicitly released; scheduler will not try to resume a Paused execution
    }

    /// <summary>
    /// Marks the execution as completed (terminal state). The record can be cleaned up by a background job.
    /// </summary>
    public void Complete()
    {
        WorkflowState = WorkflowState.Completed;
        InstanceId = string.Empty;
        LeaseExpiresAt = DateTime.MinValue;
    }

    /// <summary>
    /// Checks if the lease has expired (i.e., the executing process likely crashed or stalled).
    /// </summary>
    public bool IsLeaseExpired => DateTime.UtcNow >= LeaseExpiresAt;

    /// <summary>Clears pending domain events.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}