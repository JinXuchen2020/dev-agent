using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Evaluation.Commands.DeleteEvaluationDataset;

/// <summary>Deletes a dataset (tenant-scoped cascade removes owned cases).</summary>
public sealed record DeleteEvaluationDatasetCommand(Guid Id) : ICommand<Unit>;

internal sealed class DeleteEvaluationDatasetCommandHandler(IEvaluationDatasetRepository repository)
    : IRequestHandler<DeleteEvaluationDatasetCommand, Unit>
{
    public async Task<Unit> Handle(DeleteEvaluationDatasetCommand request, CancellationToken ct)
    {
        var dataset = await repository.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Evaluation dataset '{request.Id}' was not found.");

        repository.Remove(dataset);

        return Unit.Value;
    }
}
