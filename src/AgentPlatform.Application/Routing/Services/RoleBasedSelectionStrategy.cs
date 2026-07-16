using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// LLM-driven or role-based selection strategy for the negotiation preset (Blueprint C.5).
/// Selects the next agent/step based on role capability and current workflow context.
///
/// Fallback: selects the first incomplete step when no explicit routing is needed.
/// </summary>
public sealed class RoleBasedSelectionStrategy : ISelectionStrategy
{
    /// <summary>
    /// Selects the next step based on role capability: rework steps (critic feedback),
    /// critic review after developer steps, or default next-in-line.
    /// Implements the structured feedback loop (Blueprint C.6).
    /// </summary>
    public Task<WorkflowStep?> SelectNextAsync(
        WorkflowContext context,
        IReadOnlyList<WorkflowStep> steps,
        CancellationToken ct = default)
    {
        // 1. If there's a step that was explicitly marked for rework (critic feedback), pick it
        var reworkStep = steps.FirstOrDefault(s =>
            s.State == WorkflowState.Failed &&
            s.ErrorDetail?.Contains("CRITIC_REWORK", StringComparison.OrdinalIgnoreCase) == true);
        if (reworkStep != null)
            return Task.FromResult<WorkflowStep?>(reworkStep);

        // 2. If there's a critic step that should review recent output, pick it
        //    (a critic step is identified by "critic" in its StepName)
        var lastCompletedByOrder = steps
            .Where(s => s.State == WorkflowState.Completed)
            .OrderByDescending(s => s.Order)
            .FirstOrDefault();

        if (lastCompletedByOrder != null)
        {
            // After a developer step, route to critic (architect/tester) for review
            var criticStep = FindCriticStep(steps, lastCompletedByOrder);
            if (criticStep != null && criticStep.State == WorkflowState.Pending)
                return Task.FromResult<WorkflowStep?>(criticStep);
        }

        // 3. Default: next pending/failed step in order
        var next = steps
            .Where(s => s.State == WorkflowState.Pending || s.State == WorkflowState.Failed)
            .OrderBy(s => s.Order)
            .FirstOrDefault();

        return Task.FromResult(next);
    }

    /// <summary>
    /// Finds the appropriate critic step after a given step completes.
    /// This implements the structured-feedback loop (Blueprint C.6):
    /// Developer → Tester(critic) or Architect(critic).
    /// </summary>
    private static WorkflowStep? FindCriticStep(IReadOnlyList<WorkflowStep> steps, WorkflowStep completedStep)
    {
        var name = completedStep.StepName.ToLowerInvariant();

        // After Developer step, route to Tester or Architect for review
        if (name.Contains("developer") || name.Contains("dev"))
        {
            return steps.FirstOrDefault(s =>
                (s.StepName.Contains("tester", StringComparison.OrdinalIgnoreCase) ||
                 s.StepName.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
                 s.StepName.Contains("architect", StringComparison.OrdinalIgnoreCase)) &&
                s.State == WorkflowState.Pending);
        }

        return null;
    }
}
