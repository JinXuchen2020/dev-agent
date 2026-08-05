using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTemplates;

/// <summary>
/// Lists platform-level workflow templates with optional category + keyword filtering.
/// Templates are shared across all tenants, so this query is global (no tenant filter).
/// </summary>
/// <param name="Category">Optional category filter (决策 S4 硬编码枚举).</param>
/// <param name="Keyword">Optional keyword matched against name / description / tags.</param>
public sealed record ListWorkflowTemplatesQuery(
    WorkflowTemplateCategory? Category = null,
    string? Keyword = null) : IRequest<IReadOnlyList<WorkflowTemplateSummaryResponse>>;

internal sealed class ListWorkflowTemplatesQueryHandler(
    IWorkflowTemplateRepository repository)
    : IRequestHandler<ListWorkflowTemplatesQuery, IReadOnlyList<WorkflowTemplateSummaryResponse>>
{
    public async Task<IReadOnlyList<WorkflowTemplateSummaryResponse>> Handle(
        ListWorkflowTemplatesQuery request, CancellationToken ct)
    {
        var templates = await repository.ListAsync(request.Category, request.Keyword, ct);
        return templates.Select(MapSummary).ToList();
    }

    internal static WorkflowTemplateSummaryResponse MapSummary(WorkflowTemplate t) => new(
        t.Id, t.Name, t.Category, t.Description, t.Tags);
}
