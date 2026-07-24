using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 工具调用节点执行器（<see cref="StepType.Tool"/>）。
/// 调 <see cref="ToolCallingDispatcher"/> 真实执行平台已注册工具，结果作下游 artifact。
/// 节点配置（<c>ConfigJson</c>）：<c>toolName</c>（必填）、<c>parameters</c>（JSON 对象，作为工具入参）。
/// </summary>
internal sealed class ToolStepExecutor : IStepExecutor
{
    private readonly ILogger<ToolStepExecutor> _logger;
    private readonly ToolCallingDispatcher _dispatcher;

    public ToolStepExecutor(ILogger<ToolStepExecutor> logger, ToolCallingDispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    /// <summary>兜底 glob（不应被命中，因为显式 HandlesType 优先）。</summary>
    public string StepType => "*";

    /// <summary>显式处理工具调用节点。</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Tool;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (string.IsNullOrWhiteSpace(config.ToolName))
                return StepExecutionResult.FatalFailure("工具节点未配置 toolName");

            _logger.LogInformation("工具节点 {StepName}：调用工具 {ToolName}", step.Name, config.ToolName);

            var result = await _dispatcher.DispatchAsync(config.ToolName!, config.Parameters, ct);
            if (result.Success)
            {
                var artifact = JsonSerializer.Serialize(new { tool = config.ToolName, output = Truncate(result.Output, 2000) });
                return StepExecutionResult.Success(result.Output, artifact);
            }

            _logger.LogWarning("工具节点 {StepName}：工具 {ToolName} 执行失败：{Error}", step.Name, config.ToolName, result.ErrorMessage);
            return StepExecutionResult.RetryableFailure(result.ErrorMessage ?? "工具执行失败");
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("工具节点被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工具节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private ToolNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new ToolNodeConfig(null, "{}");

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? toolName = root.TryGetProperty("toolName", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            string parameters = "{}";
            if (root.TryGetProperty("parameters", out var p))
                parameters = p.GetRawText();
            return new ToolNodeConfig(toolName, parameters);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "工具节点配置 JSON 解析失败");
            return new ToolNodeConfig(null, "{}");
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);

    private sealed record ToolNodeConfig(string? ToolName, string Parameters);
}
