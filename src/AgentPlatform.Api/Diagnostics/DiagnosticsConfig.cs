using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentPlatform.Api.Diagnostics;

/// <summary>
/// Provides the central <see cref="ActivitySource"/> and <see cref="Meter"/> for
/// OpenTelemetry instrumentation across the Agent Platform API.
/// </summary>
public static class DiagnosticsConfig
{
    /// <summary>
    /// The service name used for tracing and metrics.
    /// </summary>
    public const string ServiceName = "AgentPlatform.Api";

    /// <summary>
    /// The service version.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// The central activity source for distributed tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    /// <summary>
    /// The central meter for metrics collection.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // ---- Metrics Instruments ----

    /// <summary>
    /// Counter for total API requests.
    /// </summary>
    public static readonly Counter<int> ApiRequestCounter = Meter.CreateCounter<int>("api.requests.total", description: "Total number of API requests");

    /// <summary>
    /// Counter for failed requests.
    /// </summary>
    public static readonly Counter<int> ApiErrorCounter = Meter.CreateCounter<int>("api.errors.total", description: "Total number of failed API requests");

    /// <summary>
    /// Histogram for request duration in milliseconds.
    /// </summary>
    public static readonly Histogram<double> ApiRequestDuration = Meter.CreateHistogram<double>(
        "api.request.duration_ms", unit: "ms", description: "API request duration in milliseconds");

    // ---- Workflow and Model Metrics ----
    // Note: Workflow and model metrics are defined in AgentPlatform.Application.Diagnostics.WorkflowMetrics
    // and recorded from Application/Infrastructure code. Only API-level metrics live here.
}
