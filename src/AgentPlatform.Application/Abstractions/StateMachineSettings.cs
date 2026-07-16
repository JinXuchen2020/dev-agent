namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration settings for the workflow state machine engine, governing retry policies,
/// step timeouts, and rollback behavior.
/// </summary>
public sealed class StateMachineSettings
{
    /// <summary>Default step timeout in seconds when no value is configured.</summary>
    public const int DefaultStepTimeoutSeconds = 120;
    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failing workflow step before rollback.
    /// Default: 3
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the timeout in seconds for each individual workflow step execution.
    /// Default: 120 (2 minutes)
    /// </summary>
    public int StepTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets the timeout in seconds for the complete rollback of a workflow.
    /// Default: 300 (5 minutes)
    /// </summary>
    public int RollbackTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the delay in milliseconds between retry attempts for a failing workflow step.
    /// Default: 1000 (1 second)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default model ID to use for agent step execution.
    /// Default: "deepseek-chat"
    /// </summary>
    public string DefaultModelId { get; set; } = "deepseek-chat";
}
