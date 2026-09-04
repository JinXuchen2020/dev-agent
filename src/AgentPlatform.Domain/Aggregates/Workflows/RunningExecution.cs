using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// Represents an in-flight workflow execution for durable scheduling.
/// Replaces the in-memory <c>ConcurrentDictionary<Guid, RunningCtsEntry></c> with a DB-backed truth source
/// that survives process restarts and enables multi-instance lease coordination.
/// </summary>
public sealed class RunningExecution : IAggregateRoot, ITenantScoped, IWorkspaceScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Gets the unique identifier of the running execution (equals WorkflowId for 1:1 mapping).</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the workflow identifier this execution belongs to.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Gets the tenant identifier that owns this execution.</summary>
    public Guid TenantId { get; private init; }
    public Guid WorkspaceId { get; private init; }

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
    /// Rehydrates a running execution from persisted state (store-loaded shape: explicit
    /// timestamps and holder). Lets callers — repositories under test, migration tools —
    /// reconstruct exact persisted semantics, including an already-expired lease for
    /// crash-recovery takeover scenarios.
    /// </summary>
    public static RunningExecution Rehydrate(
        Guid workflowId,
        Guid tenantId,
        WorkflowState workflowState,
        string instanceId,
        DateTime heartbeatAt,
        DateTime leaseExpiresAt,
        int checkpointVersion = 0,
        string? blackboardSnapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (checkpointVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpointVersion));

        return new RunningExecution
        {
            Id = workflowId,
            WorkflowId = workflowId,
            TenantId = tenantId,
            WorkflowState = workflowState,
            HeartbeatAt = heartbeatAt,
            LeaseExpiresAt = leaseExpiresAt,
            InstanceId = instanceId,
            CheckpointVersion = checkpointVersion,
            BlackboardSnapshot = blackboardSnapshot
        };
    }

    /// <summary>
    /// Attempts to acquire the lease for this instance.
    /// Acquisition succeeds when the lease is free (never held or expired) or already held by
    /// the same instance — regardless of <see cref="WorkflowState"/>, because re-running a
    /// Completed/RolledBack workflow and resuming a Paused one are legitimate transitions that
    /// must re-acquire from those terminal/paused states (F31 regression fix: the previous
    /// Running-only gate made every re-run/resume fail with "lease held by another instance").
    /// Returns false only when another live instance holds an unexpired lease.
    /// </summary>
    public bool TryAcquireLease(string instanceId, TimeSpan leaseTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (leaseTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Lease TTL must be positive.");

        var now = DateTime.UtcNow;

        // F31 fix: compare the incoming instanceId AGAINST the stored holder. The previous
        // self-comparison (property vs property) was always true, letting any instance steal
        // a live lease and silently defeating multi-instance idempotency.
        if (string.IsNullOrEmpty(this.InstanceId)
            || now >= LeaseExpiresAt
            || string.Equals(this.InstanceId, instanceId, StringComparison.Ordinal))
        {
            this.InstanceId = instanceId;
            LeaseExpiresAt = now.Add(leaseTtl);
            HeartbeatAt = now;
            return true;
        }

        return false; // Lease actively held by another instance
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