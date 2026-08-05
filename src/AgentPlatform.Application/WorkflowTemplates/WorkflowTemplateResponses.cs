using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.WorkflowTemplates;

/// <summary>List item for the template market (card grid).</summary>
public sealed record WorkflowTemplateSummaryResponse(
    Guid Id,
    string Name,
    WorkflowTemplateCategory Category,
    string? Description,
    IReadOnlyList<string> Tags);

/// <summary>Full template detail, including the preview graph (nodes / edges).</summary>
public sealed record WorkflowTemplateDetailResponse(
    Guid Id,
    string Name,
    WorkflowTemplateCategory Category,
    string? Description,
    IReadOnlyList<string> Tags,
    string Context,
    IReadOnlyList<WorkflowVersionNode> Nodes,
    IReadOnlyList<WorkflowVersionEdge> Edges);

/// <summary>A category option for dropdowns / filters.</summary>
public sealed record WorkflowTemplateCategoryOption(int Value, string Name);
