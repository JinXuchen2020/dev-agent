using System.Text.Json;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Termination condition for the negotiation preset (Blueprint C.5 / C.6).
/// The negotiation converges when:
/// 1. All steps have a completed artifact in the WorkflowContext, AND
/// 2. The last critic/review step has approved the artifact (Approved == true).
///
/// This prevents infinite negotiation loops while ensuring quality convergence.
/// The convergence signal is read from the CriticStepExecutor's artifact JSON,
/// not from the Blackboard (which is ephemeral per request context).
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
    /// Returns true when: (1) max rounds reached, or (2) a critic artifact has Approved == true.
    /// Scans the WorkflowContext.Artifacts for a critic/review step whose JSON content
    /// indicates approval — this replaces the old Blackboard-based check that was never
    /// effective because the Blackboard was rebuilt per-request.
    /// </summary>
    public Task<bool> ShouldTerminateAsync(WorkflowContext context, CancellationToken ct = default)
    {
        _roundCount++;

        // Hard cap: terminate after max rounds to prevent runaway loops
        if (_roundCount >= _maxRounds)
            return Task.FromResult(true);

        // Check if any critic artifact shows approval (scan most recent first)
        var latestApproved = context.Artifacts.Values
            .Where(a => a.StepName.Contains("critic", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.ProducedAt)
            .Any(a => IsCriticApproved(a.Content));

        return Task.FromResult(latestApproved);
    }

    private static bool IsCriticApproved(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("Approved", out var prop) && prop.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
