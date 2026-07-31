using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 基于 Jint 的安全表达式求值器（S2 决策）。
/// 在 Jint 引擎沙箱中运行表达式，仅注入 <c>artifacts</c>（上游 artifact 字典）、
/// <c>blackboard</c>（共享 Blackboard 键值）、<c>input</c>（可选输入）与内置 <c>Math</c>；
/// 默认不启用 CLR/宿主访问，脚本无法触及文件/网络/进程等 .NET API。
/// 带执行超时约束（<see cref="TimeoutInterval"/>）+ 最大语句数硬边界，防止恶意/错误表达式无限循环。
/// （Jint 4.x 已移除 3.x 的 <c>AddTimeout</c> / <c>GetCompletionValue</c> / <c>JavaScriptTimeoutException</c>，
/// 超时改用 <c>Options.TimeoutInterval</c>，结果以 <c>Engine.Evaluate</c> 返回的 <c>JsValue</c> 取得。）
/// </summary>
internal sealed class JsConditionEvaluator : IConditionEvaluator
{
    private readonly ILogger<JsConditionEvaluator> _logger;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(2);
    private const int s_maxStatements = 200_000;

    public JsConditionEvaluator(ILogger<JsConditionEvaluator> logger)
    {
        _logger = logger;
    }

    public Task<bool> EvaluateAsync(
        string expression,
        IReadOnlyDictionary<string, string> artifacts,
        IReadOnlyDictionary<string, string> blackboard,
        string? input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(blackboard);

        if (string.IsNullOrWhiteSpace(expression))
            throw new WorkflowExpressionException("表达式不能为空。");

        var artifactsJson = JsonSerializer.Serialize(artifacts);
        var blackboardJson = JsonSerializer.Serialize(blackboard);
        var inputJson = JsonSerializer.Serialize(input);

        // 把表达式包成表达式语句（加括号确保返回完成值），并以 JSON 注入安全作用域。
        // JSON 序列化会对引号/控制字符转义，防止 artifact 内容逃逸出字面量。
        var script = $"var artifacts = {artifactsJson}; var blackboard = {blackboardJson}; var input = {inputJson}; ({expression});";

        try
        {
            // Jint 4.x：沙箱默认不暴露 CLR（AllowClr=false）；
            // TimeoutInterval + MaxStatements 作为无限循环/恶意表达式的硬边界。
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(s_timeout);
                options.MaxStatements(s_maxStatements);
            });

            var completion = engine.Evaluate(script);
            var result = ToBoolean(completion);
            return Task.FromResult(result);
        }
        catch (JavaScriptException ex)
        {
            _logger.LogWarning(ex, "表达式求值失败/超时：{Expression}", expression);
            throw new WorkflowExpressionException($"表达式求值失败：{ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw; // 让上层（编排器/执行器）按取消语义处理
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "表达式求值失败：{Expression} —— {Message}", expression, ex.Message);
            throw new WorkflowExpressionException($"表达式求值失败：{ex.Message}", ex);
        }
    }

    /// <summary>按 JavaScript 真值语义将 JsValue 转换为 bool（仅空字符串/false/0/null/undefined 为假）。</summary>
    private static bool ToBoolean(JsValue value) => value.ToObject() switch
    {
        bool b => b,
        null => false,
        string s => !string.IsNullOrEmpty(s),
        double d => d != 0,
        int i => i != 0,
        long l => l != 0,
        _ => true
    };
}

/// <summary>
/// 工作流表达式求值失败（语法错误 / 超时 / 运行时异常）时抛出。
/// </summary>
public sealed class WorkflowExpressionException : Exception
{
    public WorkflowExpressionException(string message) : base(message) { }
    public WorkflowExpressionException(string message, Exception inner) : base(message, inner) { }
}
