using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.Workflows.Queries.GetWorkflow;

/// <summary>
/// Query to retrieve the full detail of a workflow by its ID, including all steps.
/// </summary>
/// <param name="Id">The workflow identifier.</param>
public sealed record GetWorkflowQuery(Guid Id) : IRequest<WorkflowDetailResponse?>;

/// <summary>
/// Full detail of a workflow including all steps.
/// </summary>
/// <param name="Id">The workflow identifier.</param>
/// <param name="Name">The workflow name.</param>
/// <param name="CurrentState">The current execution state.</param>
/// <param name="Steps">The workflow steps.</param>
/// <param name="Context">The shared context JSON.</param>
/// <param name="CreatedAt">When the workflow was created.</param>
/// <param name="UpdatedAt">When the workflow was last updated.</param>
public sealed record WorkflowDetailResponse(
    Guid Id,
    string Name,
    WorkflowState CurrentState,
    IReadOnlyList<WorkflowStepResponse> Steps,
    string Context,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Response model for a single workflow step.
/// </summary>
/// <param name="Id">The step identifier.</param>
/// <param name="Order">The zero-based step order.</param>
/// <param name="StepName">The step name.</param>
/// <param name="AssignedAgentId">The assigned agent ID, if any.</param>
/// <param name="State">The step execution state.</param>
/// <param name="Result">The step result, if any.</param>
/// <param name="ErrorDetail">The error detail, if any.</param>
public sealed record WorkflowStepResponse(
    Guid Id,
    int Order,
    string StepName,
    Guid? AssignedAgentId,
    WorkflowState State,
    string? Result,
    string? ErrorDetail);

internal sealed class GetWorkflowQueryHandler(
    Domain.Repositories.IWorkflowRepository repository)
    : IRequestHandler<GetWorkflowQuery, WorkflowDetailResponse?>
{
    public async Task<WorkflowDetailResponse?> Handle(
        GetWorkflowQuery request, CancellationToken ct)
    {
        var workflow = await repository.GetByIdAsync(request.Id, ct);
        if (workflow == null)
            return null;

        return new WorkflowDetailResponse(
            workflow.Id,
            workflow.Name,
            workflow.CurrentState,
            workflow.Steps.Select(s => new WorkflowStepResponse(
                s.Id, s.Order, s.StepName, s.AssignedAgentId, s.State, s.Result, s.ErrorDetail)).ToList(),
            workflow.Context,
            workflow.CreatedAt,
            workflow.UpdatedAt);
    }
}
