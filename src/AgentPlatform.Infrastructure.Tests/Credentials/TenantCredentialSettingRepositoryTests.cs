using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Credentials;

/// <summary>
/// EF-backed integration test for <see cref="TenantCredentialSettingRepository"/>.
/// Validates the core F13 invariants at the database layer after the multi-credential change:
/// a tenant may hold multiple credentials per category; each is persisted (the controller commits
/// via <see cref="IUnitOfWork"/>); tenant isolation holds via the central <c>HasQueryFilter</c>;
/// and only the ciphertext + prefix are stored (never plaintext).
/// </summary>
public class TenantCredentialSettingRepositoryTests
{
    private static AppDbContext CreateContext(Guid tenantId, SqliteConnection connection)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        var workspaceProvider = Substitute.For<IWorkspaceProvider>();
        workspaceProvider.GetWorkspaceId().Returns(Guid.Empty);
        tenantProvider.GetTenantId().Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options, tenantProvider, workspaceProvider);
    }

    [Fact]
    public async Task Add_Persists_Multiple_Per_Tenant_And_Enforces_Isolation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            using (var ctx = CreateContext(tenantA, connection))
            {
                ctx.Database.EnsureCreated();
            }

            // Tenant A inserts two different model credentials and commits via the unit of work.
            {
                await using var ctxA = CreateContext(tenantA, connection);
                var repo = new TenantCredentialSettingRepository(ctxA);
                await repo.AddAsync(new TenantCredentialSetting(
                    Guid.NewGuid(), tenantA, CredentialCategory.Model, "My DeepSeek", "DeepSeek",
                    "encA1", "sk-ABCD1234", "https://api.deepseek.com", "deepseek-chat", true), CancellationToken.None);
                await repo.AddAsync(new TenantCredentialSetting(
                    Guid.NewGuid(), tenantA, CredentialCategory.Model, "My GPT", "OpenAI",
                    "encA2", "sk-XYZ9876", null, "gpt-4o", true), CancellationToken.None);
                await ctxA.SaveChangesAsync(CancellationToken.None);
            }

            // Tenant B must NOT see A's credentials (HasQueryFilter tenant isolation).
            {
                await using var ctxB = CreateContext(tenantB, connection);
                var repoB = new TenantCredentialSettingRepository(ctxB);
                var fromB = await repoB.GetAllByTenantAndCategoryAsync(tenantB, CredentialCategory.Model, CancellationToken.None);
                Assert.Empty(fromB);
            }

            // Tenant A sees both persisted credentials (ciphertext + prefix, never plaintext).
            {
                await using var ctxA2 = CreateContext(tenantA, connection);
                var repoA = new TenantCredentialSettingRepository(ctxA2);
                var list = await repoA.GetAllByTenantAndCategoryAsync(tenantA, CredentialCategory.Model, CancellationToken.None);
                Assert.Equal(2, list.Count);
                var providers = list.Select(c => c.Provider).ToHashSet();
                Assert.Contains("DeepSeek", providers);
                Assert.Contains("OpenAI", providers);
                Assert.All(list, c =>
                {
                    // 仓储层原样持久化密文（加密发生在控制器层），此处验证 round-trip 与掩码前缀。
                    Assert.StartsWith("sk-", c.ApiKeyPrefix);
                    Assert.True(c.EncryptedApiKey == "encA1" || c.EncryptedApiKey == "encA2");
                });
            }

            // Update one credential in place (name + provider), the other untouched.
            {
                await using var ctxA3 = CreateContext(tenantA, connection);
                var repoA3 = new TenantCredentialSettingRepository(ctxA3);
                var list = await repoA3.GetAllByTenantAndCategoryAsync(tenantA, CredentialCategory.Model, CancellationToken.None);
                var target = list.Single(c => c.Provider == "OpenAI");
                target.Update("My GPT Renamed", "OpenAI", "encA2b", "sk-RENAMED1", null, "gpt-4o-mini", true);
                await repoA3.UpdateAsync(target, CancellationToken.None);
                await ctxA3.SaveChangesAsync(CancellationToken.None);
            }

            // Verify update applied and count unchanged (no duplicate row).
            {
                await using var ctxA4 = CreateContext(tenantA, connection);
                var repoA4 = new TenantCredentialSettingRepository(ctxA4);
                var list = await repoA4.GetAllByTenantAndCategoryAsync(tenantA, CredentialCategory.Model, CancellationToken.None);
                Assert.Equal(2, list.Count);
                var renamed = list.Single(c => c.Provider == "OpenAI");
                Assert.Equal("My GPT Renamed", renamed.Name);
                Assert.Equal("gpt-4o-mini", renamed.ModelName);
            }
        }
        finally
        {
            connection.Close();
        }
    }
}
