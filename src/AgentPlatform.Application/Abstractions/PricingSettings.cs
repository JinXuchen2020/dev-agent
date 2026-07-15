namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains per-provider pricing configuration used to estimate token costs.
/// </summary>
public sealed class PricingSettings
{
    /// <summary>
    /// Gets or sets a dictionary mapping provider names (e.g., "openai", "anthropic") to their cost per million tokens.
    /// </summary>
    public Dictionary<string, decimal> CostPerMillionTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
