namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the two built-in presets for the orchestration primitive (Blueprint C.2).
/// </summary>
public enum OrchestrationPreset
{
    /// <summary>
    /// Sequential (fast-path): fixed-order selection + termination after N steps.
    /// A degenerate case of negotiation — deterministic, low-cost, easy to replay.
    /// </summary>
    Sequential,

    /// <summary>
    /// Negotiation: LLM-driven selection + critic-based convergence termination.
    /// Supports peer review, structured diff rework, and debate.
    /// </summary>
    Negotiation
}
