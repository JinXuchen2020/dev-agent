using AgentPlatform.Application.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogSteps;

/// <summary>
/// Query to retrieve the step entries for a specific execution log.
/// </summary>
/// <param name="ExecutionLogId">The execution log identifier.</param>
/// <param name="Status">Optional filter by step status.</param>
/// <param name="Skip">Number of records to skip.</param>
/// <param name="Take">Number of records to take (max 100).</param>
public sealed record GetExecutionLogStepsQuery(
    Guid ExecutionLogId,
    WorkflowState? Status = null,
    int Skip = 0,
    int Take = 50
) : IRequest<ExecutionLogStepsResponse?>;

/// <summary>
/// Paginated list of step entries for an execution log.
/// </summary>
/// <param name="Items">The step entries.</param>
/// <param name="TotalCount">The total number of steps.</param>
public sealed record ExecutionLogStepsResponse(
    IReadOnlyList<ExecutionLogStepEntry> Items,
    int TotalCount);

internal sealed class GetExecutionLogStepsQueryHandler(
    Domain.Repositories.IExecutionLogRepository repository)
    : IRequestHandler<GetExecutionLogStepsQuery, ExecutionLogStepsResponse?>
{
    public async Task<ExecutionLogStepsResponse?> Handle(
        GetExecutionLogStepsQuery request, CancellationToken ct)
    {
        var log = await repository.GetByIdAsync(request.ExecutionLogId, ct);
        if (log == null)
            return null;

        var entries = log.Entries.AsEnumerable();

        if (request.Status.HasValue)
            entries = entries.Where(e => e.Status == request.Status.Value);

        var totalCount = entries.Count();

        var items = entries
            .OrderBy(e => e.StepOrder)
            .Skip(request.Skip)
            .Take(Math.Min(request.Take, 100))
            .Select(e => new ExecutionLogStepEntry(
                e.Id,
                e.StepName,
                e.StepOrder,
                e.Status,
                e.Duration,
                e.Result,
                e.ErrorDetail,
                e.StartedAt,
                e.CompletedAt))
            .ToList();

        return new ExecutionLogStepsResponse(items, totalCount);
    }
}
