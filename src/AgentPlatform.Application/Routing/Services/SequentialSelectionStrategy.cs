using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Fixed-order selection strategy for the sequential preset (Blueprint C.2).
/// Selects the next incomplete step in ascending order.
/// </summary>
public sealed class SequentialSelectionStrategy : ISelectionStrategy
{
    /// <summary>
    /// Selects the next pending or failed step in ascending order of <see cref="WorkflowStep.Order"/>.
    /// </summary>
    public Task<WorkflowStep?> SelectNextAsync(
        WorkflowContext context,
        IReadOnlyList<WorkflowStep> steps,
        CancellationToken ct = default)
    {
        var next = steps
            .Where(s => s.State == WorkflowState.Pending || s.State == WorkflowState.Failed)
            .OrderBy(s => s.Order)
            .FirstOrDefault();

        return Task.FromResult(next);
    }
}
