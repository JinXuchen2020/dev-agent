using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for platform-level <see cref="WorkflowTemplate"/> aggregates (F23).
/// Templates are not tenant-scoped — every method is global.
/// </summary>
public interface IWorkflowTemplateRepository
{
    /// <summary>Retrieves a template by its unique identifier.</summary>
    Task<WorkflowTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists templates with optional category and keyword (name / description / tag) filtering.
    /// Platform-level — no tenant filter is applied.
    /// </summary>
    Task<IReadOnlyList<WorkflowTemplate>> ListAsync(
        WorkflowTemplateCategory? category = null,
        string? keyword = null,
        CancellationToken ct = default);

    /// <summary>Adds a new template (used by seeding).</summary>
    void Add(WorkflowTemplate template);
}
