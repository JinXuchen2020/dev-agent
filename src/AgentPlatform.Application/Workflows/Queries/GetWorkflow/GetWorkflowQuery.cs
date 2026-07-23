using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.Workflows.Queries.GetWorkflow;

/// <summary>
/// Query to retrieve the full detail of a workflow by its ID, including steps, graph nodes, and edges.
/// </summary>
/// <param name="Id">The workflow identifier.</param>
public sealed record GetWorkflowQuery(Guid Id) : IRequest<WorkflowDetailResponse?>
{
    /// <summary>Maps a <see cref="Workflow"/> aggregate to its detail response. Shared by query and command handlers.</summary>
    internal static WorkflowDetailResponse ToDetailResponse(Workflow wf) => new(
        wf.Id,
        wf.Name,
        wf.CurrentState,
        wf.Steps.Select(s => new WorkflowStepResponse(
            s.Id, s.Order, s.StepName, s.AssignedAgentId, s.State, s.Result, s.ErrorDetail)).ToList(),
        wf.Nodes.Select(n => new WorkflowNodeResponse(
            n.Id, n.Type, n.Name, n.Order, n.PositionX, n.PositionY, n.ConfigJson,
            n.State, n.Result, n.ErrorDetail, n.AssignedAgentId)).ToList(),
        wf.Edges.Select(e => new WorkflowEdgeResponse(
            e.Id, e.SourceNodeId, e.TargetNodeId, e.Label)).ToList(),
        wf.Context,
        wf.CreatedAt,
        wf.UpdatedAt);
}

/// <summary>
/// Full detail of a workflow including steps, graph nodes, and edges.
/// </summary>
public sealed record WorkflowDetailResponse(
    Guid Id,
    string Name,
    WorkflowState CurrentState,
    IReadOnlyList<WorkflowStepResponse> Steps,
    IReadOnlyList<WorkflowNodeResponse> Nodes,
    IReadOnlyList<WorkflowEdgeResponse> Edges,
    string Context,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Response model for a single workflow step (legacy linear projection).</summary>
public sealed record WorkflowStepResponse(
    Guid Id,
    int Order,
    string StepName,
    Guid? AssignedAgentId,
    WorkflowState State,
    string? Result,
    string? ErrorDetail);

/// <summary>Response model for a single workflow graph node.</summary>
public sealed record WorkflowNodeResponse(
    Guid Id,
    StepType Type,
    string Name,
    int Order,
    double PositionX,
    double PositionY,
    string ConfigJson,
    WorkflowState State,
    string? Result,
    string? ErrorDetail,
    Guid? AssignedAgentId);

/// <summary>Response model for a single workflow graph edge.</summary>
public sealed record WorkflowEdgeResponse(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Label);

internal sealed class GetWorkflowQueryHandler(Domain.Repositories.IWorkflowRepository repository)
    : IRequestHandler<GetWorkflowQuery, WorkflowDetailResponse?>
{
    public async Task<WorkflowDetailResponse?> Handle(
        GetWorkflowQuery request, CancellationToken ct)
    {
        var workflow = await repository.GetByIdAsync(request.Id, ct);
        if (workflow == null)
            return null;

        return GetWorkflowQuery.ToDetailResponse(workflow);
    }
}
