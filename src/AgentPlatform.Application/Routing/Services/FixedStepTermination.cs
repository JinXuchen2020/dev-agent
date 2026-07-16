using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Termination condition for the sequential preset: terminates after N steps have completed.
/// </summary>
public sealed class FixedStepTermination : ITerminationCondition
{
    private readonly int _totalSteps;

    /// <summary>
    /// Initializes a new instance with the expected total number of steps.
    /// The workflow terminates once this many step artifacts have been produced.
    /// </summary>
    /// <param name="totalSteps">The number of steps before termination.</param>
    public FixedStepTermination(int totalSteps)
    {
        _totalSteps = totalSteps > 0 ? totalSteps : throw new ArgumentOutOfRangeException(nameof(totalSteps));
    }

    /// <summary>
    /// Returns true when the number of completed step artifacts reaches the configured total.
    /// </summary>
    public Task<bool> ShouldTerminateAsync(WorkflowContext context, CancellationToken ct = default)
    {
        // Terminate when all expected artifacts are produced
        var completedArtifacts = context.Artifacts.Count;
        return Task.FromResult(completedArtifacts >= _totalSteps);
    }
}
