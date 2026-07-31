using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 延迟节点执行器（<see cref="StepType.Delay"/>）。
/// 阻塞等待指定时长后继续；受 30s 硬上限保护，防止恶意长阻塞拖垮执行线程。
/// 配置（<c>ConfigJson</c>）：<c>durationMs</c>。
/// </summary>
internal sealed class DelayStepExecutor : IStepExecutor
{
    private static readonly int HardCapMs = 30_000;

    private readonly ILogger<DelayStepExecutor> _logger;

    public DelayStepExecutor(ILogger<DelayStepExecutor> logger)
    {
        _logger = logger;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Delay;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        var durationMs = ParseDuration(step.ConfigJson);
        var effective = Math.Min(Math.Max(0, durationMs), HardCapMs);

        _logger.LogInformation("延迟节点 {StepName}：等待 {Ms}ms（请求 {Req}ms）", step.Name, effective, durationMs);
        try
        {
            await Task.Delay(effective, ct);
        }
        catch (OperationCanceledException)
        {
            // 区分外部取消（整个工作流被终止）与仅本步骤超时（ct 仍有效）。
            if (ct.IsCancellationRequested)
                throw; // 上抛，由编排器按取消语义处理，不谎报成功
            return StepExecutionResult.RetryableFailure("延迟节点被取消");
        }

        var output = $"delayed {effective}ms";
        return StepExecutionResult.Success(output, JsonSerializer.Serialize(new { requestedMs = durationMs, waitedMs = effective }));
    }

    private int ParseDuration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("durationMs", out var d) && d.TryGetInt32(out var ms))
                return ms;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "延迟节点配置 JSON 解析失败");
        }
        return 0;
    }
}
