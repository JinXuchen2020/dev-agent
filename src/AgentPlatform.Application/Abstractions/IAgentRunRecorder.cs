using AgentPlatform.Domain.Aggregates.AgentRuns;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Persists and queries agent run history records so operators can browse past runs and reopen
/// their results / artifacts.
/// </summary>
public interface IAgentRunRecorder
{
    /// <summary>
    /// Records a finished run (success or failure) into history.
    /// </summary>
    Task RecordAsync(
        Guid tenantId,
        Guid agentId,
        string agentName,
        Guid runId,
        string goal,
        AgentRunStatus status,
        long durationMs,
        int iterations,
        int totalTokensIn,
        int totalTokensOut,
        int artifactCount,
        string? finalAnswer,
        string? errorMessage,
        CancellationToken ct);

    /// <summary>
    /// Lists run history for a single agent (tenant-scoped), newest first.
    /// </summary>
    Task<IReadOnlyList<AgentRunRecord>> ListByAgentAsync(
        Guid tenantId, Guid agentId, int skip, int take, CancellationToken ct);
}
