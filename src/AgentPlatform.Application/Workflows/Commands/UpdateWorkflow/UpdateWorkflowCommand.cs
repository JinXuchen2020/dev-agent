using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;

/// <summary>
/// Updates a workflow draft without executing it. All fields are optional (partial update);
/// at least one must be supplied. Implements <see cref="ICommand{T}"/> so the UnitOfWorkBehavior
/// persists the tracked aggregate (no orchestration primitive participates in the update path,
/// so there is no double-save risk).
/// </summary>
/// <param name="Id">The workflow identifier.</param>
/// <param name="Name">Optional new display name.</param>
/// <param name="InitialContext">Optional new shared context (JSON).</param>
/// <param name="Steps">Optional replacement list of step names (whole-list replace).</param>
/// <param name="TenantId">The tenant that owns the workflow (resolved by the controller).</param>
public record UpdateWorkflowCommand(
    Guid Id,
    string? Name,
    string? InitialContext,
    IReadOnlyList<string>? Steps,
    Guid TenantId
) : ICommand<WorkflowDetailResponse?>;
