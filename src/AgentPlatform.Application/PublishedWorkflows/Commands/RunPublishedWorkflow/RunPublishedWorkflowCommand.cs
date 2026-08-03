using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;

namespace AgentPlatform.Application.PublishedWorkflows.Commands.RunPublishedWorkflow;

/// <summary>
/// 按 slug 运行已发布的 API 模式工作流（F22）。供已发布工作流 HTTP 端点控制器与
/// 平台内 MCP 端点控制器（tools/call）共用。实现 <see cref="ICommand{TResponse}"/> 以经
/// UnitOfWorkBehavior 提交最终态与审计。返回 null 表示 slug 不可达（控制器映射为 404，不泄露存在性）。
/// </summary>
public record RunPublishedWorkflowCommand(
    string Slug,
    Guid TenantId,
    string? InputJson,
    Guid? InvokingKeyId = null
) : ICommand<RunPublishedWorkflowResponse?>;
