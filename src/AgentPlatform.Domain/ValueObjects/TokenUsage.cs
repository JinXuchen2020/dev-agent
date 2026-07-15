namespace AgentPlatform.Domain.ValueObjects;

/// <summary>
/// Represents the token consumption of a single model invocation, tracking
/// prompt and completion tokens separately for cost analysis.
/// </summary>
/// <param name="PromptTokens">The number of tokens consumed in the input prompt.</param>
/// <param name="CompletionTokens">The number of tokens generated in the model completion.</param>
public record TokenUsage(int PromptTokens, int CompletionTokens)
{
    /// <summary>
    /// Gets the total number of tokens consumed (prompt plus completion).
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
}
