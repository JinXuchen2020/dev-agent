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
    Code = 7,

    /// <summary>HTTP 节点：向外部服务发起真实 HTTP 请求（method/url/headers/body）。</summary>
    Http = 8,

    /// <summary>条件分支节点：用表达式求值（Jint 沙箱）选择 true/false 出边。</summary>
    Condition = 9,

    /// <summary>循环节点：对 itemsSource 的每个元素迭代执行引用的主图 body 子图。</summary>
    Loop = 10,

    /// <summary>变量节点：向共享 Blackboard 写入（set）或读取（get）键值，跨节点传递数据。</summary>
    Variable = 11,

    /// <summary>子工作流节点：触发目标工作流以独立 execution 运行，父节点仅持子流引用。</summary>
    SubWorkflow = 12,

    /// <summary>延迟节点：阻塞等待指定时长（受硬上限保护）后再继续。</summary>
    Delay = 13,

    /// <summary>人工审批门（HITL）：暂停工作流等待人工输入/批准，恢复后续跑。</summary>
    UserInput = 14,

    /// <summary>自主 Agent 节点：以 agent + goal 跑 ReAct 控制循环（F29 Agentic Agent Primitive）。</summary>
    Agentic = 15
}
