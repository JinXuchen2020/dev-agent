namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Represents a single candidate model that the router may select for handling a request.
/// </summary>
/// <param name="ModelId">The unique identifier of the model (e.g., "gpt-4o").</param>
/// <param name="Provider">The provider hosting the model (e.g., "openai", "anthropic").</param>
/// <param name="Priority">The priority value used to order candidates; higher values indicate stronger preference.</param>
public record ModelCandidate(string ModelId, string Provider, int Priority);
