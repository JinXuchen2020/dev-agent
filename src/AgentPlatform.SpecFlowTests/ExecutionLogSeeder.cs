using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 执行日志种子：经真实文件 SQLite（解析自集成宿主 DI 容器）写入 / 清空 <see cref="ExecutionLog"/> 聚合。
/// 与 HTTP 查询端点共用同一 DB 文件，满足「真 DB」契约。所有种子落 T1，确保 admin(T1) 查询可见。
/// </summary>
public static class ExecutionLogSeeder
{
    /// <summary>清空全部执行日志（忽略租户过滤器），保证每个 Scenario 起始状态确定。</summary>
    public static async Task ClearAsync(CancellationToken ct = default)
    {
        using var scope = IntegrationHost.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var all = await db.Set<ExecutionLog>().IgnoreQueryFilters().ToListAsync(ct);
        db.Set<ExecutionLog>().RemoveRange(all);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>写入一组执行日志（含其步骤条目，OwnsMany 一并持久化）。</summary>
    public static async Task SeedAsync(IEnumerable<ExecutionLog> logs, CancellationToken ct = default)
    {
        using var scope = IntegrationHost.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var log in logs)
            db.Set<ExecutionLog>().Add(log);
        await db.SaveChangesAsync(ct);
    }
}
