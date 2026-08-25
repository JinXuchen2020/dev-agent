namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration settings for durable workflow execution (F30).
/// Controls lease TTL, checkpoint batching, and recovery behavior.
/// </summary>
public sealed class DurableExecutionSettings
{
    /// <summary>
    /// Lease time-to-live in minutes. After this period without heartbeat,
    /// the workflow execution is considered stalled and eligible for recovery by another scheduler instance.
    /// Default: 5 minutes.
    /// </summary>
    public int LeaseTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of steps to accumulate before flushing a checkpoint to the database.
    /// Default: 5 steps.
    /// </summary>
    public int CheckpointBatchSize { get; set; } = 5;

    /// <summary>
    /// Maximum age of an unflushed checkpoint in seconds.
    /// Even if batch size is not reached, checkpoint is flushed after this duration.
    /// Default: 30 seconds.
    /// </summary>
    public int CheckpointMaxAgeSeconds { get; set; } = 30;
}