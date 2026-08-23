using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentRuns;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed implementation of <see cref="IAgentRunRecorder"/>.
/// </summary>
internal sealed class AgentRunRecorder : IAgentRunRecorder
{
    private readonly AppDbContext _db;

    public AgentRunRecorder(AppDbContext db)
    {
        _db = db;
    }

    public async Task RecordAsync(
        Guid tenantId,
        Guid agentId,
        string agentName,
        Guid runId,
        string goal,
        AgentRunStatus status,
        long durationMs,
        int iterations,
        int totalTokensIn,
        int totalTokensOut,
        int artifactCount,
        string? finalAnswer,
        string? errorMessage,
        CancellationToken ct)
    {
        _db.AgentRunRecords.Add(new AgentRunRecord(
            tenantId,
            agentId,
            agentName,
            runId,
            goal,
            status,
            durationMs,
            iterations,
            totalTokensIn,
            totalTokensOut,
            artifactCount,
            finalAnswer,
            errorMessage));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentRunRecord>> ListByAgentAsync(
        Guid tenantId, Guid agentId, int skip, int take, CancellationToken ct)
    {
        return await _db.AgentRunRecords
            .Where(r => r.TenantId == tenantId && r.AgentId == agentId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }
}
