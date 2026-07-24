namespace AgentPlatform.Domain;

/// <summary>
/// 当工作流图结构不合法（存在环、缺少入口/出口、节点不连通或节点重名）时抛出。
/// 由 API 层映射为 HTTP 422。
/// </summary>
public class WorkflowGraphException : Exception
{
    /// <summary>使用指定消息初始化新实例。</summary>
    public WorkflowGraphException(string message) : base(message)
    {
    }

    /// <summary>使用指定消息与内部异常初始化新实例。</summary>
    public WorkflowGraphException(string message, Exception inner) : base(message, inner)
    {
    }
}
