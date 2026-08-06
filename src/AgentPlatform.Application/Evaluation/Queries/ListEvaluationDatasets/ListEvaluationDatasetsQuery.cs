using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Evaluation.Queries.ListEvaluationDatasets;

/// <summary>Lists the caller's tenant datasets, optionally filtered by a name keyword.</summary>
public sealed record ListEvaluationDatasetsQuery(string? Keyword = null)
    : IRequest<IReadOnlyList<EvaluationDatasetSummaryResponse>>;

internal sealed class ListEvaluationDatasetsQueryHandler(
    IEvaluationDatasetRepository repository, ITenantProvider tenantProvider)
    : IRequestHandler<ListEvaluationDatasetsQuery, IReadOnlyList<EvaluationDatasetSummaryResponse>>
{
    public async Task<IReadOnlyList<EvaluationDatasetSummaryResponse>> Handle(
        ListEvaluationDatasetsQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();
        var datasets = await repository.GetByTenantAsync(tenantId, request.Keyword, ct);

        return datasets.Select(d => new EvaluationDatasetSummaryResponse(
            d.Id, d.Name, d.Description, d.Cases.Count, d.CreatedAt)).ToList();
    }
}
