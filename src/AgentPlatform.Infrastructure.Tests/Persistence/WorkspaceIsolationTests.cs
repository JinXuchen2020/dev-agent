using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workspaces;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Persistence;

/// <summary>
/// F35 workspace-isolation EF tests (real SQLite, mirrors TenantCredentialSettingRepositoryTests style):
/// · SaveChanges injects the current workspace id into added IWorkspaceScoped entities.
/// · Query filter isolates entities per workspace (same tenant, different workspaces).
/// · Workspace / WorkspaceMember are NOT workspace-filtered (their WorkspaceId is data, not scope).
/// </summary>
public class WorkspaceIsolationTests
{
    private static AppDbContext CreateContext(Guid tenantId, Guid workspaceId, SqliteConnection connection)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        var workspaceProvider = Substitute.For<IWorkspaceProvider>();
        workspaceProvider.GetWorkspaceId().Returns(workspaceId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options, tenantProvider, workspaceProvider);
    }

    [Fact]
    public async Task SaveChanges_Injects_CurrentWorkspaceId_For_Added_Entities()
    {
        var tenantId = Guid.NewGuid();
        var ws1 = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                ctx.Database.EnsureCreated();
            }

            var wfId = Guid.NewGuid();
            await using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                // Aggregate factory does not take a workspace id — injection must fill it in.
                ctx.Workflows.Add(new Workflow(wfId, "wf", tenantId));
                await ctx.SaveChangesAsync();
            }

            // Read back via a context bound to a DIFFERENT workspace (bypassing filters would be
            // needed to see the row at all; here IgnoreQueryFilters only drops tenant+ws filters).
            await using (var ctx = CreateContext(tenantId, Guid.NewGuid(), connection))
            {
                var stored = await ctx.Workflows.IgnoreQueryFilters().SingleAsync(w => w.Id == wfId);
                Assert.Equal(ws1, stored.WorkspaceId);
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task Query_Filter_Isolates_Entities_By_Workspace()
    {
        var tenantId = Guid.NewGuid();
        var ws1 = Guid.NewGuid();
        var ws2 = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                ctx.Database.EnsureCreated();
            }

            var wf1 = Guid.NewGuid();
            var wf2 = Guid.NewGuid();
            await using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                ctx.Workflows.Add(new Workflow(wf1, "wf-w1", tenantId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, ws2, connection))
            {
                ctx.Workflows.Add(new Workflow(wf2, "wf-w2", tenantId));
                await ctx.SaveChangesAsync();
            }

            // Same tenant, different workspaces: each context only sees its own rows.
            await using (var ctx1 = CreateContext(tenantId, ws1, connection))
            {
                var list = await ctx1.Workflows.ToListAsync();
                var ids = list.Select(w => w.Id).ToHashSet();
                Assert.Contains(wf1, ids);
                Assert.DoesNotContain(wf2, ids);
            }

            await using (var ctx2 = CreateContext(tenantId, ws2, connection))
            {
                var list = await ctx2.Workflows.ToListAsync();
                var ids = list.Select(w => w.Id).ToHashSet();
                Assert.Contains(wf2, ids);
                Assert.DoesNotContain(wf1, ids);
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task Workspace_And_Member_Are_Not_Workspace_Filtered()
    {
        var tenantId = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, Guid.NewGuid(), connection))
            {
                ctx.Database.EnsureCreated();
            }

            var otherWs = Guid.NewGuid();
            var wsEntityId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            await using (var ctx = CreateContext(tenantId, Guid.NewGuid(), connection))
            {
                // Workspace / WorkspaceMember carry WorkspaceId as DATA — must stay visible
                // regardless of the current workspace scope (otherwise switch validation would
                // never find the target workspace).
                ctx.Workspaces.Add(new Workspace(wsEntityId, tenantId, "Other", isDefault: false));
                ctx.WorkspaceMembers.Add(new WorkspaceMember(memberId, tenantId, otherWs, Guid.NewGuid()));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, Guid.NewGuid(), connection))
            {
                Assert.True(await ctx.Workspaces.AnyAsync(w => w.Id == wsEntityId));
                Assert.True(await ctx.WorkspaceMembers.AnyAsync(m => m.Id == memberId));
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task CountBusinessEntitiesAsync_Sums_Entities_In_Workspace()
    {
        var tenantId = Guid.NewGuid();
        var ws1 = Guid.NewGuid();
        var ws2 = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                ctx.Database.EnsureCreated();
            }

            await using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                ctx.Workflows.Add(new Workflow(Guid.NewGuid(), "wf-w1a", tenantId));
                ctx.Workflows.Add(new Workflow(Guid.NewGuid(), "wf-w1b", tenantId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, ws2, connection))
            {
                ctx.Workflows.Add(new Workflow(Guid.NewGuid(), "wf-w2", tenantId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, ws1, connection))
            {
                IWorkspaceRepository repo = new WorkspaceRepository(ctx);
                Assert.Equal(2, await repo.CountBusinessEntitiesAsync(ws1));
                Assert.Equal(1, await repo.CountBusinessEntitiesAsync(ws2));
            }
        }
        finally
        {
            connection.Dispose();
        }
    }
}
