namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Determines when the orchestration loop should terminate.
/// Used by both sequential and negotiation presets (Blueprint C.5 / C.6).
/// </summary>
public interface ITerminationCondition
{
    /// <summary>
    /// Returns true when the orchestration should stop.
    /// </summary>
    /// <param name="context">The current workflow execution context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> ShouldTerminateAsync(WorkflowContext context, CancellationToken ct = default);
}
