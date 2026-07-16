using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Determines which step/agent is selected next for execution.
/// Used by the negotiation preset (Blueprint C.5) — NOT SequentialGroupChatManager.
/// </summary>
public interface ISelectionStrategy
{
    /// <summary>
    /// Given the current workflow context, selects the next step to execute.
    /// Returns null if no step is eligible (workflow is complete or blocked).
    /// </summary>
    Task<WorkflowStep?> SelectNextAsync(WorkflowContext context, IReadOnlyList<WorkflowStep> steps, CancellationToken ct = default);
}
