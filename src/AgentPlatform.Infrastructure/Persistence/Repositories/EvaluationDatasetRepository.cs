using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IEvaluationDatasetRepository"/> (F24).
/// </summary>
internal sealed class EvaluationDatasetRepository : IEvaluationDatasetRepository
{
    private readonly AppDbContext _db;

    public EvaluationDatasetRepository(AppDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<EvaluationDataset?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.EvaluationDatasets
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EvaluationDataset>> GetByTenantAsync(
        Guid tenantId, string? keyword = null, CancellationToken ct = default)
    {
        var query = _db.EvaluationDatasets
            .Where(d => d.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(d => d.Name.Contains(keyword));

        return await query
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public void Add(EvaluationDataset dataset) => _db.EvaluationDatasets.Add(dataset);

    /// <inheritdoc/>
    public void Update(EvaluationDataset dataset) => _db.EvaluationDatasets.Update(dataset);

    /// <inheritdoc/>
    public void Remove(EvaluationDataset dataset) => _db.EvaluationDatasets.Remove(dataset);
}
