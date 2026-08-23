namespace AgentPlatform.Domain.Aggregates.AgentRuns;

/// <summary>
/// Outcome of an agent run, stored as a string column for readability.
/// </summary>
public enum AgentRunStatus
{
    Completed = 0,
    Failed = 1,
    Cancelled = 2,
}
