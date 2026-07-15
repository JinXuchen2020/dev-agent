namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the contract for orchestrating multi-agent collaboration workflows,
/// typically involving AutoGen-powered group chat with multiple specialized agent roles.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs a multi-agent collaboration session for the given input.
    /// </summary>
    /// <param name="input">The natural-language input that seeds the collaboration.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the aggregated collaboration result string.</returns>
    Task<string> RunCollaborationAsync(string input, CancellationToken ct = default);
}
