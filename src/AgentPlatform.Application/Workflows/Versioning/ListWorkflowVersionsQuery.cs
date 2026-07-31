using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>Lists versions of a workflow ordered by version number descending (paged).</summary>
/// <param name="WorkflowId">The workflow whose versions to list.</param>
/// <param name="Skip">Number of items to skip.</param>
/// <param name="Take">Number of items to take (1–100).</param>
public sealed record ListWorkflowVersionsQuery(Guid WorkflowId, int Skip = 0, int Take = 20)
    : IRequest<WorkflowVersionList>;

internal sealed class ListWorkflowVersionsQueryHandler
    : IRequestHandler<ListWorkflowVersionsQuery, WorkflowVersionList>
{
    private readonly IWorkflowVersionRepository _versionRepo;

    public ListWorkflowVersionsQueryHandler(IWorkflowVersionRepository versionRepo) =>
        _versionRepo = versionRepo;

    public async Task<WorkflowVersionList> Handle(ListWorkflowVersionsQuery request, CancellationToken ct)
    {
        var items = await _versionRepo.ListByWorkflowAsync(request.WorkflowId, request.Skip, request.Take, ct);
        var total = await _versionRepo.CountByWorkflowAsync(request.WorkflowId, ct);
        return new WorkflowVersionList(items.Select(WorkflowVersionMapper.ToSummary).ToList(), total);
    }
}
