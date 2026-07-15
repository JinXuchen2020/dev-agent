namespace AgentPlatform.Domain.ValueObjects;

/// <summary>
/// Represents the configuration of a model endpoint, including the provider,
/// model identifier, API URL, and generation parameters.
/// </summary>
/// <param name="Provider">The name of the model provider (e.g., "OpenAI", "Anthropic").</param>
/// <param name="ModelName">The identifier of the model to use (e.g., "gpt-4o").</param>
/// <param name="ApiUrl">The fully-qualified API URL for invoking the model.</param>
/// <param name="MaxTokens">The maximum number of tokens the model can generate. Defaults to 4096.</param>
/// <param name="Temperature">The sampling temperature controlling response randomness. Defaults to 0.7.</param>
public record ModelEndpoint(
    string Provider,
    string ModelName,
    string ApiUrl,
    int MaxTokens = 4096,
    double Temperature = 0.7
);
