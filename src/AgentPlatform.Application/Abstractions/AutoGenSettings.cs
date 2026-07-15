namespace AgentPlatform.Application.Abstractions;

    /// <summary>
    /// Configuration settings for AutoGen multi-agent orchestration, including agent model assignments,
    /// maximum conversation rounds, and termination conditions.
    /// </summary>
    public sealed class AutoGenSettings
{
    /// <summary>
    /// Gets or sets the maximum number of conversation rounds before the agent group chat terminates.
    /// Default: 20
    /// </summary>
    public int MaxRounds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the interval in seconds between agent turns before the conversation is considered idle.
    /// Default: 30
    /// </summary>
    public int MaxIdleIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for agent transitions.
    /// Default: 3
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the default model identifier to use for agent responses when not overridden.
    /// Default: "deepseek-chat"
    /// </summary>
    public string DefaultModelId { get; set; } = "deepseek-chat";
}
