namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration for multi-agent collaboration guards (F32 storm/livelock prevention).
/// </summary>
public sealed class AgentCollaborationSettings
{
    /// <summary>
    /// Maximum number of messages allowed per negotiation round. Exceeding the budget
    /// circuit-breaks the run (storm guard). Default: 64.
    /// </summary>
    public int MaxMessagesPerRound { get; set; } = 64;

    /// <summary>
    /// Maximum seconds a round may run without any step state advancing (stall guard).
    /// On expiry the negotiation terminates with an alert instead of hanging. Default: 120.
    /// </summary>
    public int StallTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum agents reasoning in parallel per proposal phase (bounds fan-out/fan-in load).
    /// Default: 8.
    /// </summary>
    public int MaxAgentsParallel { get; set; } = 8;

    /// <summary>
    /// How many times the same message fingerprint (sender→receiver+type+payload) may recur
    /// before it is treated as a livelock loop and circuit-broken. Default: 3.
    /// </summary>
    public int LoopFingerprintThreshold { get; set; } = 3;
}