using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ApiKeys;
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
/// </summary>
public static class IntegrationSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
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

        // ── T1 示例 Completed 工作流（发布/运行场景）──
        if (await db.Workflows.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == IntegrationConstants.SampleWorkflowId, ct) is null)
        {
            var wf = new Workflow(IntegrationConstants.SampleWorkflowId, "BDD Sample Workflow", IntegrationConstants.Tenant1Id);
            wf.ReplaceSteps(new[] { "Generate content" });
            wf.Complete();
            db.Workflows.Add(wf);
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

        // ── T1 第二条示例 Completed 工作流（MCP 列表过滤负向）──
        if (await db.Workflows.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == IntegrationConstants.SampleWorkflow3Id, ct) is null)
        {
            var wf = new Workflow(IntegrationConstants.SampleWorkflow3Id, "BDD Sample Workflow 3", IntegrationConstants.Tenant1Id);
            wf.ReplaceSteps(new[] { "Translate" });
            wf.Complete();
            db.Workflows.Add(wf);
        }

        await db.SaveChangesAsync(ct);
    }
}
