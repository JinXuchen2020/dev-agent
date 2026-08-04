using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 可控步骤执行器：在集成测试中替代全部真实 <see cref="IStepExecutor"/>，**仅**隔离外部 LLM 步骤行为
/// （合法外部依赖隔离，非 Repository mock）。真实工作流引擎（<see cref="IOrchestrationPrimitive"/>）据此执行
/// 真实的重试 / 回滚 / 暂停逻辑，并持久化到真实文件 SQLite。
///
/// 这是 BDD 层对 WorkflowStateMachine / MultiAgentPipeline 旧玩具假实现（TestStateMachineEngine /
/// TestAgentOrchestrator，均实现已废弃的 IStateMachineEngine / IAgentOrchestrator）的诚实替代——
/// 旧假实现在测试内重写了引擎逻辑，零真实覆盖；本执行器只替换「步骤做什么」，引擎本身仍是生产代码。
/// </summary>
public sealed class ConfigurableStepExecutor : IStepExecutor
{
    private readonly Dictionary<string, (StepOutcome Outcome, string Error)> _failures = new();
    private readonly ConcurrentDictionary<string, int> _callCounts = new();

    /// <inheritdoc />
    public string StepType => "*";

    /// <inheritdoc />
    public StepType? HandlesType => null;

    /// <summary>配置指定步骤名在下次执行时返回给定结果（用于模拟重试 / 回滚）。</summary>
    public void ConfigureFailure(string stepName, StepOutcome outcome, string error)
        => _failures[stepName] = (outcome, error);

    /// <summary>清除所有失败配置与调用计数（每个 Scenario 前调用，避免跨场景泄漏）。</summary>
    public void Reset()
    {
        _failures.Clear();
        _callCounts.Clear();
    }

    /// <summary>返回某步骤被真实引擎调用的次数（用于断言重试次数）。</summary>
    public int GetCallCount(string stepName)
        => _callCounts.TryGetValue(stepName, out var c) ? c : 0;

    /// <inheritdoc />
    public Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _callCounts.AddOrUpdate(step.Name, 1, (_, c) => c + 1);

        if (_failures.TryGetValue(step.Name, out var fail))
            return Task.FromResult(new StepExecutionResult(fail.Outcome, null, null, fail.Error));

        return Task.FromResult(StepExecutionResult.Success($"Output from {step.Name}", "{}"));
    }
}
