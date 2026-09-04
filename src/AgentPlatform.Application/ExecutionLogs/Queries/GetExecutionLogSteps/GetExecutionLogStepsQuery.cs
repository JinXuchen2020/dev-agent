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
    Domain.Repositories.IExecutionLogRepository repository,
    Abstractions.ITenantProvider tenantProvider)
    : IRequestHandler<GetExecutionLogStepsQuery, ExecutionLogStepsResponse?>
{
    public async Task<ExecutionLogStepsResponse?> Handle(
        GetExecutionLogStepsQuery request, CancellationToken ct)
    {
        var take = Math.Min(request.Take, 100);

        // F40 安全收口（同详情查询）：非本租户一律 null → 404，不暴露存在性。
        if (!await repository.IsOwnedByTenantAsync(
                request.ExecutionLogId, tenantProvider.GetTenantId(), ct))
        {
            return null;
        }

        var result = await repository.QueryStepsAsync(
            request.ExecutionLogId, request.Status, request.Skip, take, ct);

        if (result == null)
            return null;

        var (entries, totalCount) = result.Value;

        var items = entries
            .Select(e => new ExecutionLogStepEntry(
                e.Id,
                e.StepName,
                e.StepOrder,
                e.Status,
                e.Duration,
                e.Result,
                e.ErrorDetail,
                e.StartedAt,
                e.CompletedAt,
                e.TokensIn,
                e.TokensOut,
                e.NodeType))
            .ToList();

        return new ExecutionLogStepsResponse(items, totalCount);
    }
}
