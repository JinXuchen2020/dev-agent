using System.Collections.Generic;
using System.Linq;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Analytics.Queries.GetDashboardSummary;

/// <summary>
/// Query that returns a consolidated analytics summary for the current tenant's dashboard.
/// A single round-trip covers all KPI cards and time-series chart data.
/// </summary>
/// <param name="From">Optional inclusive start date (UTC). Defaults to 14 days before <see cref="To"/>.</param>
/// <param name="To">Optional inclusive end date (UTC). Defaults to now (UTC).</param>
public sealed record GetDashboardSummaryQuery(DateTime? From, DateTime? To)
    : IRequest<DashboardSummaryDto>;

/// <summary>
/// Consolidated dashboard payload. Day buckets are keyed by UTC date.
/// </summary>
public sealed record DashboardSummaryDto(
    DateTime From,
    DateTime To,
    DashboardKpis Kpis,
    List<ExecutionDayBucket> ExecutionsByDay,
    List<TokenDayBucket> TokenByDay,
    List<ConversationDayBucket> ConversationsByDay,
    List<LatencyDayBucket> LatencyByDay,
    List<WorkflowCount> TopWorkflows);

/// <summary>
/// Headline KPI values shown as cards.
/// </summary>
/// <param name="ActiveAgents">Total agents owned by the tenant (matches the legacy 4-card count).</param>
/// <param name="ActiveWorkflows">Total workflows owned by the tenant (matches the legacy 4-card count).</param>
/// <param name="TotalExecutions">Executions started within the selected range.</param>
/// <param name="SuccessRate">Success rate (%) over executions that reached a terminal state (completed / (completed + failed)).</param>
/// <param name="TotalTokens">Sum of conversation tokens within the selected range.</param>
/// <param name="AvgLatencyMs">Average per-execution step latency (ms) within the selected range.</param>
public sealed record DashboardKpis(
    int ActiveAgents,
    int ActiveWorkflows,
    int TotalExecutions,
    double SuccessRate,
    long TotalTokens,
    double AvgLatencyMs);

/// <summary>Per-day execution counts and success rate for the trend chart.</summary>
public sealed record ExecutionDayBucket(DateTime Date, int Completed, int Failed, int Running, double SuccessRate);

/// <summary>Per-day conversation token consumption.</summary>
public sealed record TokenDayBucket(DateTime Date, long TotalTokens);

/// <summary>Per-day conversation count.</summary>
public sealed record ConversationDayBucket(DateTime Date, int Count);

/// <summary>Per-day average execution latency (ms).</summary>
public sealed record LatencyDayBucket(DateTime Date, double AvgMs);

/// <summary>A workflow name and how many times it executed within the range (for the Top-N chart).</summary>
public sealed record WorkflowCount(string WorkflowName, int Count);

internal sealed class GetDashboardSummaryQueryHandler(
    IExecutionLogRepository executionLogRepository,
    IConversationRepository conversationRepository,
    IAgentRepository agentRepository,
    IWorkflowRepository workflowRepository,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    /// <summary>Maximum number of workflows shown in the Top-N chart (design doc C5).</summary>
    private const int TopWorkflowsLimit = 8;

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();

        var toUtc = request.To ?? DateTime.UtcNow;
        var fromUtc = request.From ?? toUtc.AddDays(-14);

        var startDate = fromUtc.Date;
        var endDate = toUtc.Date;

        var agents = await agentRepository.GetByTenantAsync(tenantId, ct);
        var workflows = await workflowRepository.GetByTenantAsync(tenantId, ct);

        // Pull the full (non-paginated) range, then constrain to the inclusive day window in memory
        // so same-day-late records are never dropped by the SQL `<=` bound.
        var logs = (await executionLogRepository.GetByTenantAsync(tenantId, startDate, endDate.AddDays(1), ct))
            .Where(l => l.StartedAt.Date >= startDate && l.StartedAt.Date <= endDate)
            .ToList();
        var conversations = (await conversationRepository.GetByTenantAsync(tenantId, startDate, endDate.AddDays(1), ct))
            .Where(c => c.CreatedAt.Date >= startDate && c.CreatedAt.Date <= endDate)
            .ToList();

        var dayCount = (int)(endDate - startDate).TotalDays + 1;
        var days = Enumerable.Range(0, dayCount).Select(i => startDate.AddDays(i)).ToList();

        var executionsByDay = new List<ExecutionDayBucket>(dayCount);
        var tokenByDay = new List<TokenDayBucket>(dayCount);
        var conversationsByDay = new List<ConversationDayBucket>(dayCount);
        var latencyByDay = new List<LatencyDayBucket>(dayCount);

        var workflowCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalCompleted = 0;
        int totalFailed = 0;
        int totalRunning = 0;
        long totalTokens = 0;
        double latencySum = 0;
        int latencyCount = 0;

        foreach (var day in days)
        {
            var dayLogs = logs.Where(l => l.StartedAt.Date == day).ToList();

            var completed = dayLogs.Count(l => l.Status == WorkflowState.Completed);
            var failed = dayLogs.Count(l => l.Status == WorkflowState.Failed);
            var running = dayLogs.Count(l => l.Status == WorkflowState.Running);
            var decided = completed + failed;
            var daySuccessRate = decided > 0 ? Math.Round(completed * 100.0 / decided, 2) : 0;
            executionsByDay.Add(new ExecutionDayBucket(day, completed, failed, running, daySuccessRate));

            var dayLatencies = dayLogs
                .Select(l => l.Entries.Sum(e => e.Duration.TotalMilliseconds))
                .Where(ms => ms > 0)
                .ToList();
            latencyByDay.Add(new LatencyDayBucket(
                day, dayLatencies.Count > 0 ? Math.Round(dayLatencies.Average(), 2) : 0));
            latencySum += dayLatencies.Sum();
            latencyCount += dayLatencies.Count;

            totalCompleted += completed;
            totalFailed += failed;
            totalRunning += running;

            foreach (var log in dayLogs)
            {
                if (!string.IsNullOrWhiteSpace(log.WorkflowName))
                {
                    workflowCounts.TryGetValue(log.WorkflowName, out var count);
                    workflowCounts[log.WorkflowName] = count + 1;
                }
            }

            var dayConversations = conversations.Where(c => c.CreatedAt.Date == day).ToList();
            conversationsByDay.Add(new ConversationDayBucket(day, dayConversations.Count));
            var dayTokens = dayConversations.Sum(c => (long)c.TotalTokenUsage.TotalTokens);
            tokenByDay.Add(new TokenDayBucket(day, dayTokens));
            totalTokens += dayTokens;
        }

        var topWorkflows = workflowCounts
            .OrderByDescending(kv => kv.Value)
            .Take(TopWorkflowsLimit)
            .Select(kv => new WorkflowCount(kv.Key, kv.Value))
            .ToList();

        var decidedTotal = totalCompleted + totalFailed;
        var kpis = new DashboardKpis(
            agents.Count,
            workflows.Count,
            logs.Count,
            decidedTotal > 0 ? Math.Round(totalCompleted * 100.0 / decidedTotal, 2) : 0,
            totalTokens,
            latencyCount > 0 ? Math.Round(latencySum / latencyCount, 2) : 0);

        return new DashboardSummaryDto(
            startDate, endDate, kpis,
            executionsByDay, tokenByDay, conversationsByDay, latencyByDay, topWorkflows);
    }
}
