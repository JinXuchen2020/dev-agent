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
    Critic = 4,

    /// <summary>知识库检索节点：从指定知识库的向量集合检索相关片段作为下游 artifact。</summary>
    Knowledge = 5,

    /// <summary>工具调用节点：调用平台已注册工具（Native/Skill/MCP），产生真实副作用。</summary>
    Tool = 6,

    /// <summary>代码执行节点：在沙箱中运行代码（python/javascript），回传真实 stdout/stderr。</summary>
    Code = 7

    // P2 reserved: Http, Condition, Loop, Variable, SubWorkflow, Delay, UserInput
}
