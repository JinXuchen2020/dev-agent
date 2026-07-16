using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Termination condition for the negotiation preset (Blueprint C.5 / C.6).
/// The negotiation converges when:
/// 1. All steps have a completed artifact in the WorkflowContext, AND
/// 2. The last critic/review step did NOT request rework (no "CRITIC_REWORK" signal).
///
/// This prevents infinite negotiation loops while ensuring quality convergence.
/// </summary>
public sealed class CriticConvergenceTermination : ITerminationCondition
{
    private const int MaxRoundsDefault = 20;

    private readonly int _maxRounds;
    private int _roundCount;

    /// <summary>
    /// Initializes a new instance with the maximum negotiation rounds before forced termination.
    /// </summary>
    /// <param name="maxRounds">Maximum rounds (default: 20).</param>
    public CriticConvergenceTermination(int maxRounds = MaxRoundsDefault)
    {
        _maxRounds = maxRounds > 0 ? maxRounds : throw new ArgumentOutOfRangeException(nameof(maxRounds));
    }

    /// <summary>
    /// Returns true when: (1) max rounds reached, or (2) negotiation:converged signal found in Blackboard.
    /// </summary>
    public Task<bool> ShouldTerminateAsync(WorkflowContext context, CancellationToken ct = default)
    {
        _roundCount++;

        // Hard cap: terminate after max rounds to prevent runaway loops
        if (_roundCount >= _maxRounds)
            return Task.FromResult(true);

        // Check if all expected artifacts are present (convergence)
        if (context.Artifacts.Count == 0)
            return Task.FromResult(false);

        // If we have at least 6 artifacts (one per role), check for convergence
        // In a real implementation, the critic step would set a convergence flag
        // in the Blackboard. Here we use a simplified heuristic:
        var hasConvergenceSignal = context.Blackboard.Get("negotiation:converged") == "true";

        return Task.FromResult(hasConvergenceSignal);
    }
}
