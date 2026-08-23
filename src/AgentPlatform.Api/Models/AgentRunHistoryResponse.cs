namespace AgentPlatform.Api.Models;

/// <summary>
/// A single entry in an agent's run history list.
/// </summary>
public record AgentRunHistoryResponse(
    Guid RunId,
    string AgentName,
    string Goal,
    string Status,
    int Iterations,
    int TotalTokensIn,
    int TotalTokensOut,
    int ArtifactCount,
    long DurationMs,
    string? FinalAnswer,
    string? ErrorMessage,
    DateTime CreatedAt);
