namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Classifies a workflow graph node by its execution semantics.
/// Replaces the old string-glob step-name matching with explicit, routeable types.
/// </summary>
public enum StepType
{
    /// <summary>Entry node. Does not invoke an LLM; marks the workflow start.</summary>
    Start = 0,

    /// <summary>Exit node. Produces a summary artifact from upstream outputs.</summary>
    End = 1,

    /// <summary>A single LLM call (default agent fallback).</summary>
    LLM = 2,

    /// <summary>An LLM call assigned to a specific agent.</summary>
    Agent = 3,

    /// <summary>A critic / review step (convergence / evaluation).</summary>
    Critic = 4

    // P2 reserved: Code, Http, Tool, Knowledge, Condition, Loop, Variable, SubWorkflow, Delay, UserInput
}
