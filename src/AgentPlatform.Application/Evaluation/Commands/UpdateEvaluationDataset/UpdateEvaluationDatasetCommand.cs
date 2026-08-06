using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Queries.GetEvaluationDataset;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Evaluation.Commands.UpdateEvaluationDataset;

/// <summary>Replaces a dataset's name, description, and full case set (PUT semantics).</summary>
public sealed record UpdateEvaluationDatasetCommand(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<CreateEvaluationCaseDto> Cases)
    : ICommand<EvaluationDatasetDetailResponse>;

internal sealed class UpdateEvaluationDatasetCommandHandler(
    IEvaluationDatasetRepository repository,
    IOptions<EvaluationSettings> evalSettings)
    : IRequestHandler<UpdateEvaluationDatasetCommand, EvaluationDatasetDetailResponse>
{
    public async Task<EvaluationDatasetDetailResponse> Handle(
        UpdateEvaluationDatasetCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var max = evalSettings.Value.MaxCases;
        if (request.Cases.Count > max)
            throw new InvalidOperationException(
                $"An evaluation dataset may contain at most {max} cases.");

        var dataset = await repository.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Evaluation dataset '{request.Id}' was not found.");

        var cases = request.Cases.Select(
            c => new EvaluationCase(Guid.NewGuid(), c.Input, c.ExpectedOutput, c.MatchMode)).ToList();
        dataset.Update(request.Name, request.Description, cases);

        repository.Update(dataset);

        return GetEvaluationDatasetQueryHandler.Map(dataset);
    }
}
