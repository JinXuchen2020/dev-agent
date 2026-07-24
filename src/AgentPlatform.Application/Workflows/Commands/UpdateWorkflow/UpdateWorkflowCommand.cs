using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;

/// <summary>
/// Updates a workflow draft without executing it. All fields are optional (partial update);
/// supplying <see cref="Nodes"/> + <see cref="Edges"/> replaces the graph (validated); otherwise
/// <see cref="Steps"/> replaces the legacy linear chain. Implements <see cref="ICommand{T}"/> so the
/// UnitOfWorkBehavior persists the tracked aggregate (no orchestration primitive participates).
/// </summary>
/// <param name="Id">The workflow identifier.</param>
/// <param name="Name">Optional new display name.</param>
/// <param name="InitialContext">Optional new shared context (JSON).</param>
/// <param name="Steps">Optional replacement list of step names (whole-list replace, legacy).</param>
/// <param name="Nodes">Optional graph node definitions (whole-graph replace).</param>
/// <param name="Edges">Optional graph edge definitions (whole-graph replace).</param>
/// <param name="TenantId">The tenant that owns the workflow (resolved by the controller).</param>
public record UpdateWorkflowCommand(
    Guid Id,
    string? Name,
    string? InitialContext,
    IReadOnlyList<string>? Steps,
    IReadOnlyList<WorkflowNodeRequest>? Nodes = null,
    IReadOnlyList<WorkflowEdgeRequest>? Edges = null,
    Guid TenantId = default
) : ICommand<WorkflowDetailResponse?>;

/// <summary>Graph node payload from the client (frontend temp id preserved for edge mapping).</summary>
public sealed record WorkflowNodeRequest(
    Guid Id,
    StepType Type,
    string Name,
    WorkflowNodePosition Position,
    string? Config = null,
    Guid? AssignedAgentId = null);

/// <summary>Canvas position payload.</summary>
public sealed record WorkflowNodePosition(double X, double Y);

/// <summary>Graph edge payload from the client.</summary>
public sealed record WorkflowEdgeRequest(Guid Id, Guid Source, Guid Target, string? Label = null);
