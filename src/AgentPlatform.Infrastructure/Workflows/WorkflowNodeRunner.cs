using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 用于 DAG 调试的单节点执行器。按 <see cref="StepType"/> 解析合适的执行器
/// （并回退到遗留的名称 glob），执行单个节点，仅持久化该节点的结果——
/// 工作流自身的状态保持不变。
/// </summary>
internal sealed class WorkflowNodeRunner : IWorkflowNodeRunner
{
    private readonly IWorkflowRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowNodeRunner> _logger;

    public WorkflowNodeRunner(
        IWorkflowRepository repository,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<WorkflowNodeRunner> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<WorkflowNodeRunResult> RunNodeAsync(Workflow workflow, Guid nodeId, CancellationToken ct)
    {
        workflow.EnsureGraphSynced();

        var node = workflow.Nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new WorkflowGraphException($"Node '{nodeId}' not found in workflow '{workflow.Id}'.");

        if (node.Type == StepType.Start)
            return new WorkflowNodeRunResult(node.Id, node.State, node.Result, node.ErrorDetail);

        var ctx = BuildContext(workflow, node);
        var executor = ResolveExecutor(node);
        if (executor == null)
        {
            node.SetError("No executor found for node: " + node.Name);
            await PersistAsync(workflow, ct);
            return new WorkflowNodeRunResult(node.Id, node.State, node.Result, node.ErrorDetail);
        }

        var result = await executor.ExecuteAsync(node, ctx, ct);
        switch (result.Outcome)
        {
            case StepOutcome.Success:
                node.SetResult(result.Output ?? "");
                break;
            case StepOutcome.NeedsIntervention:
                workflow.SetState(WorkflowState.Paused);
                break;
            default:
                node.SetError(result.ErrorMessage ?? "Step failed");
                break;
        }

        await PersistAsync(workflow, ct);
        return new WorkflowNodeRunResult(node.Id, node.State, node.Result, node.ErrorDetail);
    }

    private async Task PersistAsync(Workflow workflow, CancellationToken ct)
    {
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private WorkflowContext BuildContext(Workflow workflow, IWorkflowExecutable currentStep)
    {
        var artifacts = new Dictionary<string, StepArtifact>();
        foreach (var n in workflow.Nodes.Where(n =>
                     n.State == WorkflowState.Completed && !string.IsNullOrEmpty(n.Result) && n.Type != StepType.Start))
        {
            artifacts[n.Name] = new StepArtifact
            {
                StepName = n.Name,
                StepOrder = n.Order,
                Content = n.Result!,
                ContentType = "general"
            };
        }

        return new WorkflowContext
        {
            WorkflowId = workflow.Id,
            CurrentStepOrder = currentStep.Order,
            Artifacts = artifacts,
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = new StepHistory
            {
                Summaries = new Dictionary<int, string>(),
                MaxTokens = 0,
                EstimatedTokenCount = 0
            },
            TenantId = workflow.TenantId
        };
    }

    private IStepExecutor? ResolveExecutor(IWorkflowExecutable step)
    {
        var executors = _serviceProvider.GetServices<IStepExecutor>().ToList();
        if (step.Type.HasValue)
        {
            var byType = executors.FirstOrDefault(e => e.HandlesType == step.Type.Value);
            if (byType != null) return byType;
        }
        var exact = executors.FirstOrDefault(e => e.StepType == step.Name);
        if (exact != null) return exact;
        var wildcard = executors.FirstOrDefault(e =>
            e.StepType.Length > 1 && e.StepType.Contains('*') && IsGlobMatch(e.StepType, step.Name));
        if (wildcard != null) return wildcard;
        return executors.FirstOrDefault(e => e.StepType == "*") ?? executors.FirstOrDefault();
    }

    private static bool IsGlobMatch(string pattern, string value)
    {
        if (pattern.StartsWith('*') && pattern.EndsWith('*') && pattern.Length > 2)
            return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*') && pattern.Length > 1)
            return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*') && pattern.Length > 1)
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, value, StringComparison.Ordinal);
    }
}
