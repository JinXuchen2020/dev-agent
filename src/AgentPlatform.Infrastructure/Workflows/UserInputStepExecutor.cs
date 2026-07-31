using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 人工审批门节点执行器（<see cref="StepType.UserInput"/>，F20 S3 决策）。
/// 首次执行时创建 <see cref="HumanApproval"/>(Pending) 并持久化，返回 <see cref="StepOutcome.NeedsIntervention"/>
/// 使 <see cref="SequentialOrchestrator"/> 将工作流置为 Paused；若已存在 Pending 审批（重入），继续等待。
/// 审批经专用恢复端点解析后，节点结果被写回并续跑（详见 ResolveApprovalCommand）。
/// 配置（<c>ConfigJson</c>）：<c>prompt</c>（展示给审批人的提示语）。
/// </summary>
internal sealed class UserInputStepExecutor : IStepExecutor
{
    private readonly ILogger<UserInputStepExecutor> _logger;
    private readonly IHumanApprovalRepository _approvalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantProvider _tenantProvider;

    public UserInputStepExecutor(
        ILogger<UserInputStepExecutor> logger,
        IHumanApprovalRepository approvalRepository,
        IUnitOfWork unitOfWork,
        ITenantProvider tenantProvider)
    {
        _logger = logger;
        _approvalRepository = approvalRepository;
        _unitOfWork = unitOfWork;
        _tenantProvider = tenantProvider;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.UserInput;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            var tenantId = _tenantProvider.GetTenantId();

            // 已存在 Pending 审批（重入/重复执行防御）→ 继续等待人工处理，不重复创建。
            var existing = await _approvalRepository.GetPendingByNodeAsync(tenantId, ctx.WorkflowId, step.Name, ct);
            if (existing is not null)
            {
                _logger.LogInformation("UserInput 节点 {StepName}：存在待处理审批 {ApprovalId}，继续等待", step.Name, existing.Id);
                return StepExecutionResult.NeedsIntervention(existing.Prompt ?? "等待人工输入");
            }

            var approval = new HumanApproval(
                Guid.NewGuid(), tenantId, ctx.WorkflowId, step.Name,
                config.Prompt ?? step.Name);
            _approvalRepository.Add(approval);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("UserInput 节点 {StepName}：创建待处理审批 {ApprovalId}，工作流暂停", step.Name, approval.Id);
            return StepExecutionResult.NeedsIntervention(config.Prompt ?? "等待人工输入");
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("UserInput 节点被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UserInput 节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private UserInputNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new UserInputNodeConfig(null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? prompt = root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;
            return new UserInputNodeConfig(prompt);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "UserInput 节点配置 JSON 解析失败");
            return new UserInputNodeConfig(null);
        }
    }

    private sealed record UserInputNodeConfig(string? Prompt);
}
