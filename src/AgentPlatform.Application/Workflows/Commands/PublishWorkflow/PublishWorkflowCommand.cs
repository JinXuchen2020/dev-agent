using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Workflows.Commands.PublishWorkflow;

/// <summary>
/// 发布工作流为外部可执行能力（F22）。每工作流至多一条发布记录，重复发布替换既有。
/// 实现 <see cref="ICommand{TResponse}"/> 以经 UnitOfWorkBehavior 自动提交。
/// </summary>
public record PublishWorkflowCommand(
    Guid WorkflowId,
    PublishMode Mode,
    Guid? ApiKeyId,
    string? InputSchemaJson,
    Guid TenantId
) : ICommand<PublishStatusResponse>;
