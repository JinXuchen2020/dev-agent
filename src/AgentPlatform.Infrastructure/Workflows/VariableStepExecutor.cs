using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 变量节点执行器（<see cref="StepType.Variable"/>）。
/// 在共享 <see cref="Blackboard"/> 上读写键值，使数据可跨节点传递。
/// 配置（<c>ConfigJson</c>）：<c>mode</c>（set|get）、<c>name</c>、<c>value</c>（set 时，支持 {{name}} 占位替换）。
/// </summary>
internal sealed class VariableStepExecutor : IStepExecutor
{
    private static readonly Regex s_placeholder = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    private readonly ILogger<VariableStepExecutor> _logger;

    public VariableStepExecutor(ILogger<VariableStepExecutor> logger)
    {
        _logger = logger;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Variable;

    public Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (string.IsNullOrWhiteSpace(config.Name))
                return Task.FromResult(StepExecutionResult.FatalFailure("变量节点未配置 name"));

            if (string.Equals(config.Mode, "get", System.StringComparison.OrdinalIgnoreCase))
            {
                var value = ctx.Blackboard.Get(config.Name) ?? "";
                _logger.LogInformation("变量节点 {StepName}：get {Name} => {Value}", step.Name, config.Name, Truncate(value, 200));
                return Task.FromResult(StepExecutionResult.Success(value, JsonSerializer.Serialize(new { name = config.Name, value })));
            }

            // set（默认）
            var resolved = Substitute(config.Value ?? "", ctx);
            ctx.Blackboard.Set(config.Name, resolved);
            _logger.LogInformation("变量节点 {StepName}：set {Name} = {Value}", step.Name, config.Name, Truncate(resolved, 200));
            return Task.FromResult(StepExecutionResult.Success(resolved, JsonSerializer.Serialize(new { name = config.Name, value = resolved })));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "变量节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return Task.FromResult(StepExecutionResult.RetryableFailure(ex.Message));
        }
    }

    private static string Substitute(string template, WorkflowContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, art) in ctx.Artifacts)
            map[name] = art.Content;
        foreach (var (k, v) in ctx.Blackboard.Entries)
            map[k] = v;

        return s_placeholder.Replace(template, m =>
            map.TryGetValue(m.Groups[1].Value, out var val) ? val : m.Value);
    }

    private VariableNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new VariableNodeConfig("set", null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? mode = root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "set";
            string? name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new VariableNodeConfig(mode, name, value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "变量节点配置 JSON 解析失败");
            return new VariableNodeConfig("set", null, null);
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);

    private sealed record VariableNodeConfig(string? Mode, string? Name, string? Value);
}
