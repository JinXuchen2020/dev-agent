using System.Collections.Generic;
using System.Linq;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Analytics.Queries.GetWorkflowUsage;

/// <summary>
/// Returns per-workflow usage metrics (executions, success rate, average latency, tokens)
/// for the current tenant within an optional date range. A single round-trip covers the
/// workflow usage table; rows are ordered by execution count descending.
/// </summary>
/// <param name="From">Optional inclusive start date (UTC). Defaults to 14 days before <see cref="To"/>.</param>
/// <param name="To">Optional inclusive end date (UTC). Defaults to now (UTC).</param>
public sealed record GetWorkflowUsageQuery(DateTime? From, DateTime? To)
    : IRequest<WorkflowUsageList>;

/// <summary>Per-workflow aggregated usage for one tenant within a date range.</summary>
public sealed record WorkflowUsageDto(
    Guid WorkflowId,
    string WorkflowName,
    int Executions,
    int Completed,
    int Failed,
    double SuccessRate,
    double AvgLatencyMs,
    long TotalTokens);

/// <summary>The collection of per-workflow usage rows plus the resolved range.</summary>
public sealed record WorkflowUsageList(
    DateTime From,
    DateTime To,
    IReadOnlyList<WorkflowUsageDto> Items);

internal sealed class GetWorkflowUsageQueryHandler(
    IExecutionLogRepository executionLogRepository,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetWorkflowUsageQuery, WorkflowUsageList>
{
    /// <summary>Maximum allowed date-range span for the usage query (≈ 1 year).</summary>
    private const int MaxRangeDays = 366;

    /// <summary>Groups tenant execution logs by workflow and aggregates the metrics.</summary>
    public async Task<WorkflowUsageList> Handle(GetWorkflowUsageQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();

        var toUtc = request.To ?? System.DateTime.UtcNow;
        var fromUtc = request.From ?? toUtc.AddDays(-14);

        var startDate = fromUtc.Date;
        var endDate = toUtc.Date;

        var logs = (await executionLogRepository.GetByTenantAsync(tenantId, startDate, endDate.AddDays(1), ct))
            .Where(l => l.StartedAt.Date >= startDate && l.StartedAt.Date <= endDate)
            .ToList();

        var byWorkflow = new Dictionary<System.Guid, WorkflowUsageAccumulator>();
        foreach (var log in logs)
        {
            if (!byWorkflow.TryGetValue(log.WorkflowId, out var acc))
            {
                acc = new WorkflowUsageAccumulator(log.WorkflowId, log.WorkflowName);
                byWorkflow[log.WorkflowId] = acc;
            }

            acc.Executions++;
            if (log.Status == WorkflowState.Completed)
                acc.Completed++;
            else if (log.Status == WorkflowState.Failed)
                acc.Failed++;

            var ms = log.Entries.Sum(e => e.Duration.TotalMilliseconds);
            if (ms > 0)
            {
                acc.LatencySum += ms;
                acc.LatencyCount++;
            }

            acc.TotalTokens += log.Entries.Sum(e => (long)e.TokensIn + e.TokensOut);
        }

        var items = byWorkflow.Values
            .Select(a => a.ToDto())
            .OrderByDescending(d => d.Executions)
            .ToList();

        return new WorkflowUsageList(startDate, endDate, items);
    }

    /// <summary>Mutable per-workflow accumulator used during the grouping pass.</summary>
    private sealed class WorkflowUsageAccumulator(System.Guid workflowId, string workflowName)
    {
        public int Executions;
        public int Completed;
        public int Failed;
        public double LatencySum;
        public int LatencyCount;
        public long TotalTokens;

        public WorkflowUsageDto ToDto() => new(
            WorkflowId: workflowId,
            WorkflowName: workflowName,
            Executions: Executions,
            Completed: Completed,
            Failed: Failed,
            SuccessRate: (Completed + Failed) > 0
                ? System.Math.Round(Completed * 100.0 / (Completed + Failed), 2)
                : 0,
            AvgLatencyMs: LatencyCount > 0 ? System.Math.Round(LatencySum / LatencyCount, 2) : 0,
            TotalTokens: TotalTokens);
    }
}
