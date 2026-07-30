using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>Retrieves a single workflow version with its captured graph.</summary>
/// <param name="WorkflowId">The owning workflow (used to reject mismatched ids).</param>
/// <param name="VersionId">The version identifier.</param>
public sealed record GetWorkflowVersionQuery(Guid WorkflowId, Guid VersionId)
    : IRequest<WorkflowVersionDetail?>;

internal sealed class GetWorkflowVersionQueryHandler
    : IRequestHandler<GetWorkflowVersionQuery, WorkflowVersionDetail?>
{
    private readonly IWorkflowVersionRepository _versionRepo;

    public GetWorkflowVersionQueryHandler(IWorkflowVersionRepository versionRepo) =>
        _versionRepo = versionRepo;

    public async Task<WorkflowVersionDetail?> Handle(GetWorkflowVersionQuery request, CancellationToken ct)
    {
        var version = await _versionRepo.GetByIdAsync(request.VersionId, ct);
        if (version is null || version.WorkflowId != request.WorkflowId)
            return null;

        var snapshot = WorkflowGraphSnapshot.FromJson(version.SnapshotJson);
        return WorkflowVersionMapper.ToDetail(version, snapshot);
    }
}
