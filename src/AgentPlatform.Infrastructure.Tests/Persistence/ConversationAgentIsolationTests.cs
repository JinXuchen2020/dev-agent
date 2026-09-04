using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Persistence;

/// <summary>
/// F36 Conversation.AgentId EF 隔离测试（真 SQLite，风格对齐 WorkspaceIsolationTests）：
/// · GetByAgentAsync 按 (tenant, workflow, agent) 精确命中并复用同一会话。
/// · 不同 agent / 不同 workflow / 全局会话（AgentId=null）互不误配。
/// · 租户隔离由全局过滤器保证（跨租户不可见）。
/// </summary>
public class ConversationAgentIsolationTests
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

    private static Conversation MakeConversation(Guid tenantId, Guid workflowId, Guid? agentId) =>
        new(Guid.NewGuid(), tenantId, workflowId, agentId);

    [Fact]
    public async Task GetByAgentAsync_Matches_Exact_Pair_And_Isolates_Others()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantA, connection))
            {
                ctx.Database.EnsureCreated();
            }

            var targetId = Guid.NewGuid();
            await using (var ctx = CreateContext(tenantA, connection))
            {
                var target = MakeConversation(tenantA, workflowId, agentA);
                targetId = target.Id;
                target.AddMessage(new Message(Guid.NewGuid(), MessageRole.User, "第一轮"));
                target.AddMessage(new Message(Guid.NewGuid(), MessageRole.Agent, "第一轮回复"));
                ctx.Conversations.Add(target);

                // 同租户同 workflow、不同 agent → 不应被 GetByAgentAsync(agentA) 命中
                ctx.Conversations.Add(MakeConversation(tenantA, workflowId, agentB));
                // 同租户同 agent、不同 workflow → 不应命中
                ctx.Conversations.Add(MakeConversation(tenantA, Guid.NewGuid(), agentA));
                // 全局会话（AgentId=null，存量兼容）→ 不应命中
                ctx.Conversations.Add(MakeConversation(tenantA, workflowId, null));
                // 跨租户同 (workflow, agent) → 全局租户过滤器隔离
                ctx.Conversations.Add(MakeConversation(tenantB, workflowId, agentA));

                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantA, connection))
            {
                IConversationRepository repo = new ConversationRepository(ctx);
                var matched = await repo.GetByAgentAsync(tenantA, workflowId, agentA);

                Assert.NotNull(matched);
                Assert.Equal(targetId, matched!.Id);
                Assert.Equal(agentA, matched.AgentId);
                Assert.Equal(2, matched.Messages.Count);
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task GetByAgentAsync_Returns_Null_When_No_Agent_Conversation_Exists()
    {
        var tenantId = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Database.EnsureCreated();
            }

            await using (var ctx = CreateContext(tenantId, connection))
            {
                IConversationRepository repo = new ConversationRepository(ctx);
                var matched = await repo.GetByAgentAsync(tenantId, Guid.NewGuid(), Guid.NewGuid());

                Assert.Null(matched);
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task DuplicateAgentConversation_Insert_Is_Rejected_By_Unique_Index()
    {
        // F36 审查修复：并发同 (tenant, workflow, agent) 双步骤同时创建会话时，
        // 数据库唯一过滤索引强制后者失败（由编排层 best-effort 包裹吞掉），杜绝历史分裂双行。
        var tenantId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Database.EnsureCreated();
            }

            await using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Conversations.Add(MakeConversation(tenantId, workflowId, agentId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Conversations.Add(MakeConversation(tenantId, workflowId, agentId));
                await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task AgentConversation_RoundTrips_AgentId_Column()
    {
        var tenantId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Database.EnsureCreated();
            }

            await using (var ctx = CreateContext(tenantId, connection))
            {
                ctx.Conversations.Add(new Conversation(conversationId, tenantId, workflowId, agentId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(tenantId, connection))
            {
                var stored = await ctx.Conversations.AsNoTracking().SingleAsync(c => c.Id == conversationId);
                Assert.Equal(agentId, stored.AgentId);
            }
        }
        finally
        {
            connection.Dispose();
        }
    }
}
