using System.Diagnostics.Metrics;

namespace AgentPlatform.Application.Diagnostics;

/// <summary>
/// Provides OpenTelemetry metrics instruments for workflow and model-level observability.
/// These counters and histograms feed into the Prometheus scraping endpoint at /metrics.
/// </summary>
public static class WorkflowMetrics
{
    /// <summary>
    /// The meter name used for workflow and model metrics.
    /// </summary>
    public const string MeterName = "AgentPlatform.Application";

    /// <summary>
    /// The central meter for application-level metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // ---- Additional Blueprint Metrics (Phase 3) ----

    /// <summary>
    /// Histogram for active workflow step duration in milliseconds.
    /// Blueprint §8.1: workflow_step_duration_ms (additional gauge variant)
    /// </summary>
    public static readonly Histogram<double> ActiveStepsHistogram = Meter.CreateHistogram<double>(
        "workflow.active_steps",
        unit: "steps",
        description: "Number of active steps across all workflows");

    // ---- Workflow Metrics ----

    /// <summary>
    /// Histogram for workflow step execution duration in milliseconds.
    /// Tagged by step name and workflow ID.
    /// Blueprint §8.1: workflow_step_duration_ms
    /// </summary>
    public static readonly Histogram<double> WorkflowStepDuration = Meter.CreateHistogram<double>(
        "workflow.step.duration_ms",
        unit: "ms",
        description: "Workflow step execution duration");

    /// <summary>
    /// Counter for workflow completions tagged by result (success/rolledback — 失败路径经回滚落
    /// "rolledback"，代码中不存在 result="failed"；F39 告警口径依此).
    /// Blueprint §8.1: workflow_success_rate (via rate())
    /// </summary>
    public static readonly Counter<int> WorkflowCompletedCounter = Meter.CreateCounter<int>(
        "workflow.completed.total",
        description: "Total number of completed workflows by result");

    // ---- Model Metrics ----

    /// <summary>
    /// Counter for model API calls tagged by model name and provider.
    /// Blueprint §8.1: model_call_total
    /// </summary>
    public static readonly Counter<int> ModelCallCounter = Meter.CreateCounter<int>(
        "model.call.total",
        description: "Total number of model API calls by provider/model");

    /// <summary>
    /// Histogram for model call duration in milliseconds.
    /// Blueprint §8.1: model_call_duration_ms
    /// </summary>
    public static readonly Histogram<double> ModelCallDuration = Meter.CreateHistogram<double>(
        "model.call.duration_ms",
        unit: "ms",
        description: "Model API call duration");

    // ── F39 可观测性补充：评估门禁与执行队列 ──

    /// <summary>
    /// 评估门禁判定计数（F34/F39），标签 <c>passed="true"|"false"</c>。
    /// 标签保持低基数（不带 dataset/workflow GUID）；阻断率告警用
    /// <c>sum(rate(evaluation_gate_total{passed="false"}[5m])) / sum(rate(evaluation_gate_total[5m]))</c>，
    /// 与 HTTP 422 派生口径互补（本指标语义最精确，不依赖 path 正则）。
    /// </summary>
    public static readonly Counter<int> EvaluationGateCounter = Meter.CreateCounter<int>(
        "evaluation.gate.total",
        description: "Total number of evaluation gate verdicts by pass/block");

    /// <summary>
    /// 执行队列积压深度仪表名（F37/F39）。Prometheus 侧为
    /// <c>execution_queue_depth</c>，标签 <c>backend</c> = "InMemory" | "RedisStream" | "RabbitMQ"。
    /// 由各后端实现在构造期于 <see cref="Meter"/> 上创建 ObservableGauge（当前运行时仪表不可 Dispose，
    /// 生命周期随静态 Meter 覆盖整个应用期；生产侧队列为应用期单例，无重复注册问题）。
    /// 回调闭包捕获 DI 解析出的队列实例，杜绝静态可变状态。
    /// </summary>
    public const string QueueDepthInstrumentName = "execution.queue.depth";

    /// <summary>队列深度仪表说明（供各后端创建 gauge 时复用）。</summary>
    public const string QueueDepthDescription = "Pending workflow-run jobs in the execution queue";
}
