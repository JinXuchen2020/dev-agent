using AgentPlatform.Application.Evaluation;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Evaluation.Queries.GetEvaluationDataset;

/// <summary>Retrieves a single dataset including its cases.</summary>
public sealed record GetEvaluationDatasetQuery(Guid Id) : IRequest<EvaluationDatasetDetailResponse>;

internal sealed class GetEvaluationDatasetQueryHandler(IEvaluationDatasetRepository repository)
    : IRequestHandler<GetEvaluationDatasetQuery, EvaluationDatasetDetailResponse>
{
    public async Task<EvaluationDatasetDetailResponse> Handle(
        GetEvaluationDatasetQuery request, CancellationToken ct)
    {
        var dataset = await repository.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Evaluation dataset '{request.Id}' was not found.");

        return Map(dataset);
    }

    internal static EvaluationDatasetDetailResponse Map(EvaluationDataset d) => new(
        d.Id, d.Name, d.Description,
        d.Cases.Select(c => new EvaluationCaseResponse(c.Id, c.Input, c.ExpectedOutput, c.MatchMode)).ToList(),
        d.CreatedAt);
}
