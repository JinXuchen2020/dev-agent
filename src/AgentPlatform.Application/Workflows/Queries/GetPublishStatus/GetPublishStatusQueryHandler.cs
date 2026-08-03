using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Queries.GetPublishStatus;

internal sealed class GetPublishStatusQueryHandler
    : IRequestHandler<GetPublishStatusQuery, PublishStatusResponse?>
{
    private readonly IPublishedWorkflowRepository _publishedRepo;

    public GetPublishStatusQueryHandler(IPublishedWorkflowRepository publishedRepo)
    {
        _publishedRepo = publishedRepo;
    }

    public async Task<PublishStatusResponse?> Handle(GetPublishStatusQuery request, CancellationToken ct)
    {
        var e = await _publishedRepo.GetByWorkflowIdAsync(request.TenantId, request.WorkflowId, ct);
        return e is null ? null : ToStatusResponse(e);
    }

    private static PublishStatusResponse ToStatusResponse(PublishedWorkflow e) =>
        new(e.Id, e.WorkflowId, e.Slug, e.Mode.ToString(), e.IsEnabled, e.ApiKeyId, e.InputSchemaJson, e.CreatedAt);
}
