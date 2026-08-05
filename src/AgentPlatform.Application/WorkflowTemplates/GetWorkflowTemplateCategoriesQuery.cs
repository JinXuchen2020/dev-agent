using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.WorkflowTemplates;

/// <summary>
/// Returns all template categories (硬编码枚举 决策 S4) for the market filter / dropdown.
/// </summary>
public sealed record GetWorkflowTemplateCategoriesQuery()
    : IRequest<IReadOnlyList<WorkflowTemplateCategoryOption>>;

internal sealed class GetWorkflowTemplateCategoriesQueryHandler
    : IRequestHandler<GetWorkflowTemplateCategoriesQuery, IReadOnlyList<WorkflowTemplateCategoryOption>>
{
    public Task<IReadOnlyList<WorkflowTemplateCategoryOption>> Handle(
        GetWorkflowTemplateCategoriesQuery request, CancellationToken ct)
    {
        var options = Enum.GetValues<WorkflowTemplateCategory>()
            .Select(c => new WorkflowTemplateCategoryOption((int)c, c.ToString()))
            .ToList();
        return Task.FromResult<IReadOnlyList<WorkflowTemplateCategoryOption>>(options);
    }
}
