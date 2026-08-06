using AgentPlatform.Domain.Aggregates.Evaluation;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Persistence and query operations for <see cref="EvaluationDataset"/> aggregates (F24).
/// Tenant isolation is enforced by the global query filter (EvaluationDataset is ITenantScoped).
/// </summary>
public interface IEvaluationDatasetRepository
{
    /// <summary>Retrieves a dataset by id (tenant-scoped via the global filter), including its cases.</summary>
    Task<EvaluationDataset?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Retrieves all datasets for a tenant, optionally filtered by a name keyword.</summary>
    Task<IReadOnlyList<EvaluationDataset>> GetByTenantAsync(
        Guid tenantId, string? keyword = null, CancellationToken ct = default);

    /// <summary>Adds a new dataset.</summary>
    void Add(EvaluationDataset dataset);

    /// <summary>Updates an existing dataset.</summary>
    void Update(EvaluationDataset dataset);

    /// <summary>Removes a dataset (cascade removes owned cases).</summary>
    void Remove(EvaluationDataset dataset);
}
