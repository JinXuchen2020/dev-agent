using System.Collections.Generic;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 条件/循环表达式求值器。在隔离的脚本引擎中求值布尔表达式，
/// 仅暴露显式注入的安全作用域（上游 artifact / 共享 Blackboard / 输入 / Math），
/// 不暴露任何 .NET 宿主 API，禁止文件/网络/进程等副作用。
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// 求值表达式并返回布尔结果。
    /// </summary>
    /// <param name="expression">JS 风格布尔表达式，例如 <c>artifacts['status'] == 'ok' &amp;&amp; blackboard.count &gt; 3</c>。</param>
    /// <param name="artifacts">上游已完成节点的输出，键为节点名，值为文本内容。</param>
    /// <param name="blackboard">共享 Blackboard 的当前键值。</param>
    /// <param name="input">可选输入（如循环当前元素、HITL 输入），以字符串暴露为 <c>input</c>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表达式的布尔结果。</returns>
    Task<bool> EvaluateAsync(
        string expression,
        IReadOnlyDictionary<string, string> artifacts,
        IReadOnlyDictionary<string, string> blackboard,
        string? input,
        CancellationToken ct = default);
}
