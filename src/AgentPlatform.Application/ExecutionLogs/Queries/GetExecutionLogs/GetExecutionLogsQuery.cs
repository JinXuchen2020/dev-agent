using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogs;

/// <summary>
/// Queries execution logs with optional filtering by status, date range, and pagination.
/// The tenant scope is resolved by the handler from <see cref="ITenantProvider"/>.
/// </summary>
/// <param name="Status">Optional filter by workflow execution status.</param>
/// <param name="From">Optional start of the date range (inclusive).</param>
/// <param name="To">Optional end of the date range (inclusive).</param>
/// <param name="Skip">Number of records to skip (default: 0).</param>
/// <param name="Take">Number of records to take (default: 20, max: 100).</param>
public sealed record GetExecutionLogsQuery(
    WorkflowState? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Skip = 0,
    int Take = 20
) : IRequest<ExecutionLogListResponse>;

/// <summary>
/// Paginated response containing execution log summaries.
/// </summary>
/// <param name="Items">The list of execution log summaries for the current page.</param>
/// <param name="TotalCount">The total number of logs matching the filter criteria.</param>
public sealed record ExecutionLogListResponse(
    IReadOnlyList<ExecutionLogSummary> Items,
    int TotalCount
);

/// <summary>
/// Summary representation of an <see cref="ExecutionLog"/> for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the execution log.</param>
/// <param name="WorkflowId">The identifier of the workflow.</param>
/// <param name="WorkflowName">The name of the workflow.</param>
/// <param name="Status">The overall execution status.</param>
/// <param name="TotalSteps">The total number of steps.</param>
/// <param name="CompletedSteps">The number of steps that completed successfully.</param>
/// <param name="FailedSteps">The number of steps that failed.</param>
/// <param name="StartedAt">The UTC start timestamp.</param>
/// <param name="CompletedAt">The UTC completion timestamp, if any.</param>
public sealed record ExecutionLogSummary(
    Guid Id,
    Guid WorkflowId,
    string WorkflowName,
    WorkflowState Status,
    int TotalSteps,
    int CompletedSteps,
    int FailedSteps,
    DateTime StartedAt,
    DateTime? CompletedAt
);

internal sealed class GetExecutionLogsQueryHandler(
    Domain.Repositories.IExecutionLogRepository repository,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetExecutionLogsQuery, ExecutionLogListResponse>
{
    public async Task<ExecutionLogListResponse> Handle(
        GetExecutionLogsQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();
        var take = Math.Clamp(request.Take, 1, 100);

        var (items, totalCount) = await repository.QueryAsync(
            tenantId,
            request.Status,
            request.From,
            request.To,
            request.Skip,
            take,
            ct);

        var summaries = items.Select(log => new ExecutionLogSummary(
            log.Id,
            log.WorkflowId,
            log.WorkflowName,
            log.Status,
            log.TotalSteps,
            log.Entries.Count(e => e.Status == WorkflowState.Completed),
            log.Entries.Count(e => e.Status == WorkflowState.Failed),
            log.StartedAt,
            log.CompletedAt
        )).ToList();

        return new ExecutionLogListResponse(summaries, totalCount);
    }
}
