using System.Collections.Generic;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 条件分支节点执行器（<see cref="StepType.Condition"/>）。
/// 用 <see cref="IConditionEvaluator"/>（Jint 沙箱）求值布尔表达式，
/// 返回 Output="true"/"false"，由 <see cref="SequentialOrchestrator"/> 据此跳过非选中分支。
/// 配置（<c>ConfigJson</c>）：<c>expression</c>。
/// </summary>
internal sealed class ConditionStepExecutor : IStepExecutor
{
    private readonly ILogger<ConditionStepExecutor> _logger;
    private readonly IConditionEvaluator _evaluator;

    public ConditionStepExecutor(ILogger<ConditionStepExecutor> logger, IConditionEvaluator evaluator)
    {
        _logger = logger;
        _evaluator = evaluator;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Condition;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (string.IsNullOrWhiteSpace(config.Expression))
                return StepExecutionResult.FatalFailure("条件节点未配置 expression");

            var artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, art) in ctx.Artifacts)
                artifacts[name] = art.Content;

            var branch = await _evaluator.EvaluateAsync(config.Expression, artifacts, ctx.Blackboard.Entries, null, ct);
            var label = branch ? "true" : "false";

            _logger.LogInformation("条件节点 {StepName}：表达式 {Expr} => {Branch}", step.Name, config.Expression, label);
            return StepExecutionResult.Success(label, JsonSerializer.Serialize(new { expression = config.Expression, branch = label }));
        }
        catch (WorkflowExpressionException ex)
        {
            _logger.LogWarning(ex, "条件节点 {StepName} 表达式错误：{Message}", step.Name, ex.Message);
            return StepExecutionResult.FatalFailure(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("条件节点被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "条件节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private ConditionNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new ConditionNodeConfig(null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? expression = root.TryGetProperty("expression", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() : null;
            return new ConditionNodeConfig(expression);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "条件节点配置 JSON 解析失败");
            return new ConditionNodeConfig(null);
        }
    }

    private sealed record ConditionNodeConfig(string? Expression);
}
