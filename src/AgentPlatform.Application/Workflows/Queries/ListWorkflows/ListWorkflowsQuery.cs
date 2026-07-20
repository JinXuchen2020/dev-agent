using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.Workflows.Queries.ListWorkflows;

/// <summary>
/// Query to retrieve a paginated list of workflows with optional status filter.
/// </summary>
/// <param name="Status">Optional filter by workflow state.</param>
/// <param name="Skip">Number of records to skip.</param>
/// <param name="Take">Number of records to take (max 100).</param>
public sealed record ListWorkflowsQuery(
    WorkflowState? Status = null,
    int Skip = 0,
    int Take = 20
) : IRequest<WorkflowListResponse>;

/// <summary>
/// Paginated response containing workflow summaries.
/// </summary>
/// <param name="Items">The list of workflow summaries.</param>
/// <param name="TotalCount">The total number of matching workflows.</param>
public sealed record WorkflowListResponse(
    IReadOnlyList<WorkflowSummary> Items,
    int TotalCount);

/// <summary>
/// Summary representation of a workflow for list responses.
/// </summary>
/// <param name="Id">The workflow identifier.</param>
/// <param name="Name">The workflow name.</param>
/// <param name="CurrentState">The current execution state.</param>
/// <param name="StepCount">The number of steps.</param>
/// <param name="CreatedAt">When the workflow was created.</param>
/// <param name="UpdatedAt">When the workflow was last updated.</param>
public sealed record WorkflowSummary(
    Guid Id,
    string Name,
    WorkflowState CurrentState,
    int StepCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal sealed class ListWorkflowsQueryHandler(
    Domain.Repositories.IWorkflowRepository repository,
    ITenantProvider tenantProvider)
    : IRequestHandler<ListWorkflowsQuery, WorkflowListResponse>
{
    public async Task<WorkflowListResponse> Handle(
        ListWorkflowsQuery request, CancellationToken ct)
    {
        if (request.Take < 1 || request.Take > 100)
            throw new ArgumentOutOfRangeException(nameof(request.Take), "Take must be between 1 and 100.");

        var tenantId = tenantProvider.GetTenantId();
        var take = Math.Min(request.Take, 100);

        var (items, totalCount) = await repository.QueryAsync(
            tenantId, request.Status, request.Skip, take, ct);

        return new WorkflowListResponse(
            items.Select(w => new WorkflowSummary(
                w.Id, w.Name, w.CurrentState, w.Steps.Count, w.CreatedAt, w.UpdatedAt))
            .ToList(),
            totalCount);
    }
}
