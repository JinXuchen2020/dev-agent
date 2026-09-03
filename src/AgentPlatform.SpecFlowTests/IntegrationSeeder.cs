using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 集成测试种子：在 DatabaseInitializer 的基础种子（admin 用户 / 内建角色 / agent 配置）之上，
/// 插入 BDD 场景所需的专用数据——第二租户用户、T1/T2 已知明文 ApiKey、T1/T2 已完成示例工作流。
/// 已知明文密钥经 <see cref="IApiKeyEncryptionService"/> 加密落库，与运行时认证路径一致。
/// F35：先为 T1/T2 幂等供应默认工作空间；T1 实体按解析链落 T1 默认工作空间，
/// T2 实体在显式 OverrideWorkspaceId 的独立 scope 中创建（避免被注上 T1 的工作空间）。
/// </summary>
public static class IntegrationSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        Guid tenant2DefaultWorkspaceId;
        using (var scope = services.CreateScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<IWorkspaceProvisioner>();
            await provisioner.EnsureDefaultWorkspaceAsync(IntegrationConstants.Tenant1Id, ct);
            tenant2DefaultWorkspaceId = await provisioner.EnsureDefaultWorkspaceAsync(IntegrationConstants.Tenant2Id, ct);
        }

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var encryption = scope.ServiceProvider.GetRequiredService<IApiKeyEncryptionService>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // ── T1 非 Admin 用户（RBAC 403 负向场景用，role=development）──
            if (await db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email == IntegrationConstants.NonAdminEmail, ct) is null)
            {
                db.Users.Add(new User(
                    Guid.NewGuid(),
                    IntegrationConstants.Tenant1Id,
                    IntegrationConstants.NonAdminEmail,
                    hasher.Hash(IntegrationConstants.NonAdminPassword),
                    "development"));
            }

            // ── T1 ApiKey（明文经加密服务落库）──
            if (await db.ApiKeys.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(k => k.Id == IntegrationConstants.T1ApiKeyId, ct) is null)
            {
                var (encrypted, prefix) = encryption.EncryptKey(IntegrationConstants.T1ApiKeyPlaintext);
                db.ApiKeys.Add(new ApiKey(
                    IntegrationConstants.T1ApiKeyId,
                    IntegrationConstants.Tenant1Id,
                    encrypted,
                    prefix,
                    "Integration T1 Key",
                    "Admin",
                    null));
            }

            // ── T1 示例 Completed 工作流（发布/运行场景）──
            if (await db.Workflows.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == IntegrationConstants.SampleWorkflowId, ct) is null)
            {
                var wf = new Workflow(IntegrationConstants.SampleWorkflowId, "BDD Sample Workflow", IntegrationConstants.Tenant1Id);
                wf.ReplaceSteps(new[] { "Generate content" });
                wf.Complete();
                db.Workflows.Add(wf);
            }

            // ── T1 第二条示例 Completed 工作流（MCP 列表过滤负向）──
            if (await db.Workflows.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == IntegrationConstants.SampleWorkflow3Id, ct) is null)
            {
                var wf = new Workflow(IntegrationConstants.SampleWorkflow3Id, "BDD Sample Workflow 3", IntegrationConstants.Tenant1Id);
                wf.ReplaceSteps(new[] { "Translate" });
                wf.Complete();
                db.Workflows.Add(wf);
            }

            // ── T1 agent 归属会话（F36 per-agent 对话隔离 BDD 用）──
            // 归属 DatabaseInitializer 播种的 F29 demo agent（默认租户 = T1），挂种子工作流。
            if (await db.Conversations.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == IntegrationConstants.AgentConversationId, ct) is null)
            {
                var conv = new Conversation(
                    IntegrationConstants.AgentConversationId,
                    IntegrationConstants.Tenant1Id,
                    IntegrationConstants.SampleWorkflowId,
                    IntegrationConstants.F29DemoAgentId);
                conv.AddMessage(new Message(Guid.NewGuid(), MessageRole.User, "BDD agent 对话种子消息"));
                conv.AddMessage(new Message(Guid.NewGuid(), MessageRole.Agent, "BDD agent 对话种子回复"));
                db.Conversations.Add(conv);
            }

            // ── T1 失败执行日志（F40 回放诊断 BDD/E2E 用）──
            // 两个已完成节点 + 一个失败节点（带 ErrorDetail）+ 末次检查点（上下文快照可解析）。
            if (await db.Set<ExecutionLog>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(l => l.Id == IntegrationConstants.FailedExecutionLogId, ct) is null)
            {
                var log = new ExecutionLog(
                    IntegrationConstants.FailedExecutionLogId,
                    IntegrationConstants.SampleWorkflowId,
                    "BDD Failing Workflow",
                    IntegrationConstants.Tenant1Id,
                    totalSteps: 3);
                log.AddEntry(new ExecutionLogEntry(
                    Guid.NewGuid(), "BDD Start Step", 0, WorkflowState.Completed,
                    TimeSpan.FromMilliseconds(45), "kickoff", null, 12, 4, StepType.Start));
                log.AddEntry(new ExecutionLogEntry(
                    Guid.NewGuid(), "BDD Generate Step", 1, WorkflowState.Completed,
                    TimeSpan.FromMilliseconds(1200), "generated draft", null, 220, 96, StepType.LLM));
                log.AddEntry(new ExecutionLogEntry(
                    Guid.NewGuid(), IntegrationConstants.FailedExecutionStepName, 2, WorkflowState.Failed,
                    TimeSpan.FromMilliseconds(80), null, "模型返回超限：expected 1, got 0", 0, 0, StepType.Critic));
                log.Fail();
                log.UpdateCheckpoint(
                    "{\"SchemaVersion\":1,\"CheckpointVersion\":2,\"Blackboard\":{\"loop.x\":\"1\",\"trigger\":\"{}\"},"
                    + "\"ExecutionOrderIndex\":2,\"LoopBodyIndices\":{},\"SkipSet\":[],\"StepStates\":[],"
                    + "\"TenantId\":\"00000000-0000-0000-0000-000000000001\","
                    + "\"WorkflowId\":\"11111111-1111-1111-1111-111111111102\","
                    + "\"CapturedAt\":\"2026-09-01T00:00:00.000000Z\"}");
                db.Set<ExecutionLog>().Add(log);
            }

            await db.SaveChangesAsync(ct);
        }

        // ── T2 实体：独立 scope，显式 OverrideWorkspaceId = T2 默认工作空间，
        // 使 AppDbContext 的 SaveChanges 注入落到 T2 的工作空间（而非 T1 的默认解析）。──
        using (var scope = services.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<IWorkspaceContext>();
            workspaceContext.OverrideWorkspaceId = tenant2DefaultWorkspaceId;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var encryption = scope.ServiceProvider.GetRequiredService<IApiKeyEncryptionService>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // ── T2 用户（跨租户负向场景用）──
            if (await db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email == IntegrationConstants.Tenant2UserEmail, ct) is null)
            {
                db.Users.Add(new User(
                    Guid.NewGuid(),
                    IntegrationConstants.Tenant2Id,
                    IntegrationConstants.Tenant2UserEmail,
                    hasher.Hash(IntegrationConstants.Tenant2UserPassword),
                    "Admin"));
            }

            // ── T2 ApiKey ──
            if (await db.ApiKeys.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(k => k.Id == IntegrationConstants.T2ApiKeyId, ct) is null)
            {
                var (encrypted, prefix) = encryption.EncryptKey(IntegrationConstants.T2ApiKeyPlaintext);
                db.ApiKeys.Add(new ApiKey(
                    IntegrationConstants.T2ApiKeyId,
                    IntegrationConstants.Tenant2Id,
                    encrypted,
                    prefix,
                    "Integration T2 Key",
                    "Admin",
                    null));
            }

            // ── T2 示例 Completed 工作流（跨租户负向场景，需先经 T2 发布）──
            if (await db.Workflows.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == IntegrationConstants.SampleWorkflow2Id, ct) is null)
            {
                var wf = new Workflow(IntegrationConstants.SampleWorkflow2Id, "BDD Sample Workflow 2", IntegrationConstants.Tenant2Id);
                wf.ReplaceSteps(new[] { "Summarize" });
                wf.Complete();
                db.Workflows.Add(wf);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
