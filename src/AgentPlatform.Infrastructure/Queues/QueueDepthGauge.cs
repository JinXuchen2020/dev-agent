using System.Diagnostics.Metrics;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;

namespace AgentPlatform.Infrastructure.Queues;

/// <summary>
/// 执行队列深度仪表注册器（F39）。在 <see cref="WorkflowMetrics.Meter"/> 上注册
/// <c>execution.queue.depth</c> ObservableGauge，回调读取所属队列的
/// <see cref="IExecutionQueue.QueueDepth"/> 并打 <c>backend</c> 标签（低基数，仅三个固定值）。
/// 由各后端实现在构造期注册一次：回调闭包捕获 DI 解析出的队列实例，不使用静态可变状态。
/// 说明：当前运行时（net9.0）的 <see cref="ObservableGauge{T}"/> 未公开 Dispose，仪表生命周期
/// 由 <see cref="Meter"/> 终身持有；生产侧队列为应用期单例，不构成泄漏。代价是：测试中反复构造
/// 队列会累积同 (<c>name</c>, <c>backend</c>) 标签的陈旧仪表（已 dispose 队列继续上报其终值），
/// 断言须用 Contains 语义容忍，或队列实例全程唯一。
/// </summary>
internal static class QueueDepthGauge
{
    public static void Register(IExecutionQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        WorkflowMetrics.Meter.CreateObservableGauge(
            WorkflowMetrics.QueueDepthInstrumentName,
            () =>
            {
                var depth = queue.QueueDepth;
                return new[]
                {
                    new Measurement<long>(
                        depth < 0 ? 0 : depth,
                        new KeyValuePair<string, object?>("backend", queue.Backend)),
                };
            },
            unit: "jobs",
            description: WorkflowMetrics.QueueDepthDescription);
    }
}
