using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 用于调试的单节点执行器：在不推进或完成整个工作流的前提下，单独运行某个节点。
/// </summary>
public interface IWorkflowNodeRunner
{
    /// <summary>执行由 <paramref name="nodeId"/> 标识的节点并持久化其运行结果。</summary>
    Task<WorkflowNodeRunResult> RunNodeAsync(Workflow workflow, Guid nodeId, CancellationToken ct);
}

/// <summary>单节点运行（调试 / 变量检视）的结果。</summary>
public record WorkflowNodeRunResult(Guid NodeId, WorkflowState State, string? Result, string? ErrorDetail);
