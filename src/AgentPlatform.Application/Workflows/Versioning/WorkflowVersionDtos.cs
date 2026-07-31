using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>Read-only view of a single node inside a version snapshot.</summary>
public sealed record WorkflowVersionNodeView(
    Guid Id, StepType Type, string Name, double X, double Y, string? ConfigJson, Guid? AssignedAgentId);

/// <summary>Read-only view of a single edge inside a version snapshot.</summary>
public sealed record WorkflowVersionEdgeView(Guid Id, Guid Source, Guid Target, string? Label);

/// <summary>Summary of a version for list displays.</summary>
public sealed record WorkflowVersionSummary(
    Guid Id, int VersionNumber, string Name, string? Note, DateTime CreatedAt, Guid? CreatedBy);

/// <summary>Full detail of a version, including the captured graph.</summary>
public sealed record WorkflowVersionDetail(
    Guid Id, int VersionNumber, string Name, string? Note, DateTime CreatedAt, Guid? CreatedBy,
    string Context, IReadOnlyList<WorkflowVersionNodeView> Nodes, IReadOnlyList<WorkflowVersionEdgeView> Edges);

/// <summary>Paged list of versions.</summary>
public sealed record WorkflowVersionList(IReadOnlyList<WorkflowVersionSummary> Items, int TotalCount);

/// <summary>Exported workflow definition. Shares the import request shape so it can be re-imported directly.</summary>
public sealed record WorkflowExport(
    Guid Id, string Name, string Context,
    IReadOnlyList<WorkflowNodeRequest> Nodes, IReadOnlyList<WorkflowEdgeRequest> Edges, DateTime ExportedAt);

/// <summary>Request to import a workflow definition as a new workflow.</summary>
public sealed record ImportWorkflowRequest(
    string Name,
    string InitialContext,
    IReadOnlyList<WorkflowNodeRequest>? Nodes = null,
    IReadOnlyList<WorkflowEdgeRequest>? Edges = null);
