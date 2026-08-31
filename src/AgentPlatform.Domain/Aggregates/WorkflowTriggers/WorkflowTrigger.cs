using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.WorkflowTriggers;

/// <summary>
/// 工作流触发器聚合根（ITenantScoped）。一个工作流至多一个 Webhook 触发器与一个 Schedule
/// 触发器，以 <see cref="TriggerType"/> 区分；类型相关字段按需可空。
/// </summary>
public sealed class WorkflowTrigger : ITenantScoped, IWorkspaceScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the trigger.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the identifier of the workflow this trigger belongs to.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Gets the identifier of the tenant that owns this trigger.</summary>
    public Guid TenantId { get; private init; }
    public Guid WorkspaceId { get; private init; }

    /// <summary>Gets the type of the trigger (Webhook or Schedule).</summary>
    public TriggerType Type { get; private set; }

    /// <summary>Gets the unguessable webhook token (only for <see cref="TriggerType.Webhook"/>).</summary>
    public string? TriggerToken { get; private set; }

    /// <summary>Gets the cron expression (only for <see cref="TriggerType.Schedule"/>).</summary>
    public string? Cron { get; private set; }

    /// <summary>Gets the IANA timezone id used to evaluate the cron schedule (Schedule only).</summary>
    public string? Timezone { get; private set; }

    /// <summary>Gets whether the trigger is currently enabled.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Gets the UTC timestamp of the last scheduled execution (Schedule only).</summary>
    public DateTime? LastRunAt { get; private set; }

    /// <summary>Gets the precomputed next run time in UTC (Schedule only); null when disabled or invalid.</summary>
    public DateTime? NextRunAt { get; private set; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the UTC last-update time.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <inheritdoc />
    public void ClearDomainEvents() { }

    private WorkflowTrigger() { }

    private WorkflowTrigger(Guid id, Guid workflowId, Guid tenantId, TriggerType type)
    {
        Id = id;
        WorkflowId = workflowId;
        TenantId = tenantId;
        Type = type;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Creates (or represents) a Webhook trigger for the given workflow.</summary>
    public static WorkflowTrigger CreateWebhook(Guid id, Guid workflowId, Guid tenantId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new WorkflowTrigger(id, workflowId, tenantId, TriggerType.Webhook)
        {
            TriggerToken = token,
            Enabled = true
        };
    }

    /// <summary>Creates a Schedule trigger for the given workflow with a precomputed next run time.</summary>
    public static WorkflowTrigger CreateSchedule(
        Guid id, Guid workflowId, Guid tenantId, string cron, string timezone, bool enabled, DateTime? nextRunAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);
        return new WorkflowTrigger(id, workflowId, tenantId, TriggerType.Schedule)
        {
            Cron = cron,
            Timezone = timezone,
            Enabled = enabled,
            NextRunAt = nextRunAt
        };
    }

    /// <summary>Rotates the webhook token (old token becomes invalid immediately).</summary>
    public void SetWebhookToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        TriggerToken = token;
        Enabled = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Updates the schedule configuration and recomputed next-run time.</summary>
    public void UpdateSchedule(string cron, string timezone, bool enabled, DateTime? nextRunAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);
        Cron = cron;
        Timezone = timezone;
        Enabled = enabled;
        NextRunAt = nextRunAt;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Sets only the enabled flag (used to pause/resume a trigger without touching config).</summary>
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a scheduled execution, advancing the last-run and next-run markers.</summary>
    public void MarkScheduledRun(DateTime lastRunAt, DateTime? nextRunAt)
    {
        LastRunAt = lastRunAt;
        NextRunAt = nextRunAt;
        UpdatedAt = DateTime.UtcNow;
    }
}
