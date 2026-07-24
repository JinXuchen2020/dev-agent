using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 代码执行节点执行器（<see cref="StepType.Code"/>）。
/// 调 <see cref="ICodeSandbox"/> 真实运行代码，stdout/stderr/ExitCode 作下游 artifact。
/// 节点配置（<c>ConfigJson</c>）：<c>code</c>、<c>language</c>（python/javascript）、<c>timeoutSeconds</c>。
/// </summary>
internal sealed class CodeStepExecutor : IStepExecutor
{
    private readonly ILogger<CodeStepExecutor> _logger;
    private readonly ICodeSandbox _sandbox;
    private readonly SandboxSettings _settings;

    public CodeStepExecutor(ILogger<CodeStepExecutor> logger, ICodeSandbox sandbox, IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _sandbox = sandbox;
        _settings = settings.Value;
    }

    /// <summary>兜底 glob（不应被命中，因为显式 HandlesType 优先）。</summary>
    public string StepType => "*";

    /// <summary>显式处理代码执行节点。</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Code;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (string.IsNullOrWhiteSpace(config.Code))
                return StepExecutionResult.FatalFailure("代码节点未配置 code");
            if (string.IsNullOrWhiteSpace(config.Language))
                return StepExecutionResult.FatalFailure("代码节点未配置 language");

            _logger.LogInformation("代码节点 {StepName}：运行 {Language} 代码", step.Name, config.Language);

            var result = await _sandbox.RunCodeAsync(
                config.Code!, config.Language!, config.TimeoutSeconds ?? _settings.TimeoutSeconds, ct);

            var artifact = JsonSerializer.Serialize(new
            {
                language = config.Language,
                exitCode = result.ExitCode,
                stdout = Truncate(result.Stdout, 4000),
                stderr = Truncate(result.Stderr, 2000),
            });

            if (result.Success && result.ExitCode == 0)
                return StepExecutionResult.Success(result.Stdout, artifact);

            var err = string.IsNullOrWhiteSpace(result.Stderr) ? $"退出码 {result.ExitCode}" : result.Stderr;
            _logger.LogWarning("代码节点 {StepName} 执行失败（exit={Exit}）：{Err}", step.Name, result.ExitCode, err);
            return StepExecutionResult.RetryableFailure(err);
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("代码节点被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "代码节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private CodeNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new CodeNodeConfig(null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            string? language = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
            int? timeout = root.TryGetProperty("timeoutSeconds", out var t) && t.TryGetInt32(out var ti) ? ti : null;
            return new CodeNodeConfig(code, language, timeout);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "代码节点配置 JSON 解析失败");
            return new CodeNodeConfig(null, null, null);
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);

    private sealed record CodeNodeConfig(string? Code, string? Language, int? TimeoutSeconds);
}
