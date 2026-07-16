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
    /// Counter for workflow completions tagged by result (success/failed/rolledback).
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
}
