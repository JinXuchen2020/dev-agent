using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Queries.GetEvaluationDataset;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Evaluation.Commands.CreateEvaluationDataset;

/// <summary>Creates a new evaluation dataset (tenant-scoped).</summary>
public sealed record CreateEvaluationDatasetCommand(
    string Name,
    string? Description,
    IReadOnlyList<CreateEvaluationCaseDto> Cases)
    : ICommand<EvaluationDatasetDetailResponse>;

internal sealed class CreateEvaluationDatasetCommandHandler(
    IEvaluationDatasetRepository repository,
    ITenantProvider tenantProvider,
    IOptions<EvaluationSettings> evalSettings)
    : IRequestHandler<CreateEvaluationDatasetCommand, EvaluationDatasetDetailResponse>
{
    public Task<EvaluationDatasetDetailResponse> Handle(
        CreateEvaluationDatasetCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var max = evalSettings.Value.MaxCases;
        if (request.Cases.Count > max)
            throw new InvalidOperationException(
                $"An evaluation dataset may contain at most {max} cases.");

        var tenantId = tenantProvider.GetTenantId();
        var dataset = new EvaluationDataset(Guid.NewGuid(), tenantId, request.Name, request.Description);

        var cases = request.Cases.Select(
            c => new EvaluationCase(Guid.NewGuid(), c.Input, c.ExpectedOutput, c.MatchMode)).ToList();
        dataset.Update(request.Name, request.Description, cases);

        repository.Add(dataset);

        return Task.FromResult(GetEvaluationDatasetQueryHandler.Map(dataset));
    }
}
