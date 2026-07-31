using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Domain.Repositories;

/// <summary>Repository for workflow version snapshots.</summary>
public interface IWorkflowVersionRepository
{
    /// <summary>Gets a version by its identifier (tenant-filtered by the DbContext).</summary>
    Task<WorkflowVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists versions for a workflow ordered by version number descending.</summary>
    Task<IReadOnlyList<WorkflowVersion>> ListByWorkflowAsync(
        Guid workflowId, int skip = 0, int take = 20, CancellationToken ct = default);

    /// <summary>Counts versions for a workflow.</summary>
    Task<int> CountByWorkflowAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>Returns the highest version number for a workflow (0 if none).</summary>
    Task<int> GetLatestVersionNumberAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>Tracks a new version for insertion.</summary>
    void Add(WorkflowVersion version);

    /// <summary>Tracks a version for removal.</summary>
    void Remove(WorkflowVersion version);
}
