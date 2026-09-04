using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Persistence;

/// <summary>
/// F40 租户收口 EF 测试（真 SQLite）：<see cref="ExecutionLog"/> 未实现 ITenantScoped，
/// 全局 query filter 不覆盖它，故按 id 直读的端点必须走租户作用域方法。
/// 守：<c>GetByIdForTenantAsync</c> 跨租户不可读、<c>IsOwnedByTenantAsync</c> 判定正确、
/// 原 <c>GetByIdAsync</c> 仍为无过滤的内部路径（仅供已知可信上下文使用）。
/// </summary>
public sealed class ExecutionLogTenantScopeTests
{
    private static AppDbContext CreateContext(Guid tenantId, SqliteConnection connection)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        var workspaceProvider = Substitute.For<IWorkspaceProvider>();
        workspaceProvider.GetWorkspaceId().Returns(Guid.Empty);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options, tenantProvider, workspaceProvider);
    }

    [Fact]
    public async Task GetByIdForTenantAsync_Blocks_CrossTenant_Read_And_Own_Tenant_Succeeds()
    {
        var ownerTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(ownerTenant, connection))
            {
                ctx.Database.EnsureCreated();
            }

            var logId = Guid.NewGuid();
            await using (var ctx = CreateContext(ownerTenant, connection))
            {
                var log = new ExecutionLog(logId, workflowId, "owner-wf", ownerTenant, totalSteps: 2);
                log.AddEntry(new ExecutionLogEntry(
                    Guid.NewGuid(), "Generate", 0, WorkflowState.Completed,
                    TimeSpan.FromMilliseconds(30), "draft", null, 12, 4, StepType.LLM));
                log.Fail();
                ctx.Set<ExecutionLog>().Add(log);
                await ctx.SaveChangesAsync();
            }

            await using var ownerCtx = CreateContext(ownerTenant, connection);
            {
                IExecutionLogRepository repo = new ExecutionLogRepository(ownerCtx);

                var own = await repo.GetByIdForTenantAsync(logId, ownerTenant);
                Assert.NotNull(own);
                Assert.Equal("owner-wf", own!.WorkflowName);
                Assert.Single(own.Entries);

                // 他租户持同一 GUID：null（→ 404，不暴露存在性），且归属判定同为 false。
                Assert.Null(await repo.GetByIdForTenantAsync(logId, otherTenant));
                Assert.True(await repo.IsOwnedByTenantAsync(logId, ownerTenant));
                Assert.False(await repo.IsOwnedByTenantAsync(logId, otherTenant));

                // 回归护栏：既有无过滤读取仍能拿到该实体 —— 说明收口必须发生在查询侧，
                // 不能误以为全局过滤器已保护 ExecutionLog。
                Assert.NotNull(await repo.GetByIdAsync(logId));
            }
        }
        finally
        {
            connection.Dispose();
        }
    }
}
