using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 子工作流节点执行器（<see cref="StepType.SubWorkflow"/>，S4 决策）。
/// 触发目标工作流以<b>独立 execution</b>（独立 <c>ExecutionLog</c>、独立上下文、可独立 Trace）运行，
/// 父节点仅记录子流引用（childExecutionId / childWorkflowId / childStatus），不阻塞等待子流输出、不消费其产物。
/// 目标工作流受租户隔离约束（仓储 HasQueryFilter），跨租户调用将被拒绝。
/// 配置（<c>ConfigJson</c>）：<c>workflowId</c>、<c>inputMapping</c>（v1 预留，暂不消费）。
/// </summary>
internal sealed class SubWorkflowStepExecutor : IStepExecutor
{
    private readonly ILogger<SubWorkflowStepExecutor> _logger;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrchestrationPrimitive _orchestration;

    public SubWorkflowStepExecutor(
        ILogger<SubWorkflowStepExecutor> logger,
        IWorkflowRepository workflowRepository,
        IUnitOfWork unitOfWork,
        IOrchestrationPrimitive orchestration)
    {
        _logger = logger;
        _workflowRepository = workflowRepository;
        _unitOfWork = unitOfWork;
        _orchestration = orchestration;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.SubWorkflow;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (!config.WorkflowId.HasValue)
                return StepExecutionResult.FatalFailure("子工作流节点未配置 workflowId");

            var child = await _workflowRepository.GetByIdAsync(config.WorkflowId.Value, ct);
            if (child is null)
                return StepExecutionResult.FatalFailure($"子工作流 {config.WorkflowId} 不存在或不在当前租户");

            // 独立 execution：复用子工作流的持久化聚合，重置后作为独立运行跑一遍。
            if (child.CurrentState != WorkflowState.Pending && child.CurrentState != WorkflowState.Running)
                child.Reset();

            _logger.LogInformation("子工作流节点 {StepName}：触发独立 execution 工作流 {ChildId}", step.Name, child.Id);
            var ran = await _orchestration.RunAsync(child, OrchestrationPreset.Sequential, ct);

            var reference = JsonSerializer.Serialize(new
            {
                childWorkflowId = child.Id,
                childWorkflowName = child.Name,
                childStatus = ran.CurrentState.ToString()
            });

            _logger.LogInformation("子工作流节点 {StepName}：子流 {ChildId} 完成，状态 {State}", step.Name, child.Id, ran.CurrentState);
            return StepExecutionResult.Success(reference, reference);
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("子工作流节点被取消");
        }
        catch (Exception ex)
        {
            // 子流失败不应级联拖垮父流：记录失败引用并继续。
            _logger.LogError(ex, "子工作流节点 {StepName} 触发失败：{Message}", step.Name, ex.Message);
            var failed = JsonSerializer.Serialize(new { error = ex.Message });
            return StepExecutionResult.Success(failed, failed);
        }
    }

    private SubWorkflowNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new SubWorkflowNodeConfig(null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            Guid? workflowId = null;
            if (root.TryGetProperty("workflowId", out var w) && w.ValueKind == JsonValueKind.String
                && Guid.TryParse(w.GetString(), out var gid))
            {
                workflowId = gid;
            }
            return new SubWorkflowNodeConfig(workflowId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "子工作流节点配置 JSON 解析失败");
            return new SubWorkflowNodeConfig(null);
        }
    }

    private sealed record SubWorkflowNodeConfig(Guid? WorkflowId);
}
