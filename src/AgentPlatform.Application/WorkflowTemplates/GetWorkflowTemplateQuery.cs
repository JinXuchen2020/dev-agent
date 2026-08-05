using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTemplates;

/// <summary>
/// Retrieves a single template with its preview graph (nodes / edges decoded from the snapshot).
/// </summary>
/// <param name="Id">The template identifier.</param>
public sealed record GetWorkflowTemplateQuery(Guid Id)
    : IRequest<WorkflowTemplateDetailResponse?>;

internal sealed class GetWorkflowTemplateQueryHandler(
    IWorkflowTemplateRepository repository)
    : IRequestHandler<GetWorkflowTemplateQuery, WorkflowTemplateDetailResponse?>
{
    public async Task<WorkflowTemplateDetailResponse?> Handle(
        GetWorkflowTemplateQuery request, CancellationToken ct)
    {
        var template = await repository.GetByIdAsync(request.Id, ct);
        if (template is null)
            return null;

        var snapshot = WorkflowGraphSnapshot.FromJson(template.SnapshotJson);
        return new WorkflowTemplateDetailResponse(
            template.Id,
            template.Name,
            template.Category,
            template.Description,
            template.Tags,
            snapshot.Context,
            snapshot.Nodes,
            snapshot.Edges);
    }
}
