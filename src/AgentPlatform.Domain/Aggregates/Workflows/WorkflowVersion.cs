using System.Collections.Generic;
using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// 工作流定义的不可变快照（版本）。每次「存为版本」产生一条记录，可供历史查看与回滚。
/// 实现 <see cref="ITenantScoped"/> 自动获得多租户隔离（AppDbContext 全局 query filter）。
/// </summary>
public sealed class WorkflowVersion : ITenantScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the version.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the identifier of the workflow this version belongs to.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Gets the tenant that owns the version (drives the query filter).</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the monotonically increasing version number within the workflow.</summary>
    public int VersionNumber { get; private init; }

    /// <summary>Gets the workflow display name captured at snapshot time.</summary>
    public string Name { get; private init; } = null!;

    /// <summary>Gets the serialized graph snapshot (context + nodes + edges) as JSON.</summary>
    public string SnapshotJson { get; private init; } = null!;

    /// <summary>Gets an optional human note attached to the version.</summary>
    public string? Note { get; private init; }

    /// <summary>Gets the UTC creation timestamp of the version.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the user who created the version, if available.</summary>
    public Guid? CreatedBy { get; private init; }

    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Gets the collection of domain events raised by this aggregate.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Clears pending domain events after dispatch.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    private WorkflowVersion() { }

    /// <summary>Creates a new workflow version snapshot.</summary>
    public static WorkflowVersion Create(
        Guid id,
        Guid workflowId,
        Guid tenantId,
        int versionNumber,
        string name,
        string snapshotJson,
        Guid? createdBy,
        string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);

        return new WorkflowVersion
        {
            Id = id,
            WorkflowId = workflowId,
            TenantId = tenantId,
            VersionNumber = versionNumber,
            Name = name,
            SnapshotJson = snapshotJson,
            Note = note,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
    }
}
