using AgentPlatform.Application.Abstractions;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.UnpublishWorkflow;

/// <summary>
/// 取消发布工作流（F22）。幂等：若未发布则无操作。实现 <see cref="ICommand{TResponse}"/> 以自动提交。
/// </summary>
public record UnpublishWorkflowCommand(
    Guid WorkflowId,
    Guid TenantId
) : ICommand<Unit>;
