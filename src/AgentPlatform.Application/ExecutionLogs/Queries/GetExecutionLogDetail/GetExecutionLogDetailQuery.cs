using AgentPlatform.Application.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogDetail;

/// <summary>
/// Query to retrieve the full detail of an execution log including all step entries.
/// </summary>
/// <param name="Id">The unique identifier of the execution log.</param>
public sealed record GetExecutionLogDetailQuery(Guid Id) : IRequest<ExecutionLogDetailResponse?>;

/// <summary>
/// Full detail of an execution log including all step entries.
/// </summary>
/// <param name="Id">The execution log identifier.</param>
/// <param name="WorkflowId">The workflow identifier.</param>
/// <param name="WorkflowName">The workflow name.</param>
/// <param name="Status">The overall execution status.</param>
/// <param name="TotalSteps">Total number of steps.</param>
/// <param name="StartedAt">When execution started.</param>
/// <param name="CompletedAt">When execution completed.</param>
/// <param name="Entries">The step execution entries.</param>
public sealed record ExecutionLogDetailResponse(
    Guid Id,
    Guid WorkflowId,
    string WorkflowName,
    WorkflowState Status,
    int TotalSteps,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ExecutionLogStepEntry> Entries);

internal sealed class GetExecutionLogDetailQueryHandler(
    Domain.Repositories.IExecutionLogRepository repository)
    : IRequestHandler<GetExecutionLogDetailQuery, ExecutionLogDetailResponse?>
{
    public async Task<ExecutionLogDetailResponse?> Handle(
        GetExecutionLogDetailQuery request, CancellationToken ct)
    {
        var log = await repository.GetByIdAsync(request.Id, ct);
        if (log == null)
            return null;

        return new ExecutionLogDetailResponse(
            log.Id,
            log.WorkflowId,
            log.WorkflowName,
            log.Status,
            log.TotalSteps,
            log.StartedAt,
            log.CompletedAt,
            log.Entries.Select(e => new ExecutionLogStepEntry(
                e.Id,
                e.StepName,
                e.StepOrder,
                e.Status,
                e.Duration,
                e.Result,
                e.ErrorDetail,
                e.StartedAt,
                e.CompletedAt)).ToList());
    }
}
