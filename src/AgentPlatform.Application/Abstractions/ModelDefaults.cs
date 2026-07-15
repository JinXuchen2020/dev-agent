namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains default model configuration used when creating new agents or conversations without explicit overrides.
/// </summary>
public sealed class ModelDefaults
{
    /// <summary>
    /// Gets or sets the default model provider (e.g., "deepseek").
    /// </summary>
    public string ModelProvider { get; set; } = "deepseek";

    /// <summary>
    /// Gets or sets the default model name (e.g., "deepseek-chat").
    /// </summary>
    public string ModelName { get; set; } = "deepseek-chat";

    /// <summary>
    /// Gets or sets the default API base URL for the model provider.
    /// </summary>
    public string ModelApiUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>
    /// Gets or sets the default system prompt sent with each conversation.
    /// </summary>
    public string SystemPrompt { get; set; } = "You are a helpful AI assistant.";
}
