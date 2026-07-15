namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration settings for the execution log system, controlling retention,
/// batch writing behavior, and optional SSE streaming.
/// </summary>
public sealed class ExecutionLogSettings
{
    /// <summary>
    /// Gets or sets the number of days to retain execution log entries before automatic cleanup.
    /// Default: 90
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Gets or sets the maximum number of log entries buffered before a batch write is triggered.
    /// Default: 50
    /// </summary>
    public int BatchWriteThreshold { get; set; } = 50;

    /// <summary>
    /// Gets or sets whether Server-Sent Events (SSE) streaming is enabled for real-time log updates.
    /// Default: false
    /// </summary>
    public bool SseEnabled { get; set; } = false;
}
