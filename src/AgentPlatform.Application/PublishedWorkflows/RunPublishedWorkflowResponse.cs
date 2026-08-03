namespace AgentPlatform.Application.PublishedWorkflows;

/// <summary>
/// 调用已发布工作流的执行结果（F22，S4=仅最终输出）。
/// <see cref="Output"/> 为工作流运行后的最终共享上下文（blackboard JSON）。
/// </summary>
public sealed record RunPublishedWorkflowResponse(
    Guid WorkflowId,
    string Slug,
    string Status,
    string Output,
    string? ErrorMessage);
