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
/// Validates the core F13 invariants at the database layer: the credential is actually
/// persisted (the controller commits via <see cref="IUnitOfWork"/>), tenant isolation holds via
/// the central <c>HasQueryFilter</c>, upsert updates in place (no duplicate row), and only the
/// ciphertext + prefix are stored (never plaintext).
/// </summary>
public class TenantCredentialSettingRepositoryTests
{
    private static AppDbContext CreateContext(Guid tenantId, SqliteConnection connection)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task Upsert_Persists_And_Enforces_Tenant_Isolation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            // Build schema from the model (mirrors the generated EF migration for the new aggregate).
            using (var ctx = CreateContext(tenantA, connection))
            {
                ctx.Database.EnsureCreated();
            }

            // Tenant A inserts a model credential and commits via the unit of work
            // (mirrors TenantCredentialsController.Put: repo.UpsertAsync + IUnitOfWork.SaveChangesAsync).
            {
                await using var ctxA = CreateContext(tenantA, connection);
                var repo = new TenantCredentialSettingRepository(ctxA);
                var setting = new TenantCredentialSetting(
                    Guid.NewGuid(), tenantA, CredentialCategory.Model, "DeepSeek",
                    "encA", "sk-ABCD1234", "https://api.deepseek.com", "deepseek-chat", true);
                await repo.UpsertAsync(setting, CancellationToken.None);
                await ctxA.SaveChangesAsync(CancellationToken.None);
            }

            // Tenant B must NOT see A's credential (HasQueryFilter tenant isolation).
            {
                await using var ctxB = CreateContext(tenantB, connection);
                var repoB = new TenantCredentialSettingRepository(ctxB);
                var fromB = await repoB.GetByTenantAndCategoryAsync(tenantB, CredentialCategory.Model, CancellationToken.None);
                Assert.Null(fromB);
            }

            // Tenant A sees its own persisted credential (ciphertext + prefix, never plaintext).
            {
                await using var ctxA2 = CreateContext(tenantA, connection);
                var repoA = new TenantCredentialSettingRepository(ctxA2);
                var fromA = await repoA.GetByTenantAndCategoryAsync(tenantA, CredentialCategory.Model, CancellationToken.None);
                Assert.NotNull(fromA);
                Assert.Equal("DeepSeek", fromA!.Provider);
                Assert.Equal("encA", fromA.EncryptedApiKey);
                Assert.Equal("sk-ABCD1234", fromA.ApiKeyPrefix);

                // Upsert again => update in place (no second row).
                var updated = new TenantCredentialSetting(
                    Guid.NewGuid(), tenantA, CredentialCategory.Model, "OpenAI",
                    "encA2", "sk-XYZ9876", null, "gpt-4o", true);
                await repoA.UpsertAsync(updated, CancellationToken.None);
                await ctxA2.SaveChangesAsync(CancellationToken.None);
            }

            // Exactly one row for tenant A, reflecting the update.
            {
                await using var ctxA3 = CreateContext(tenantA, connection);
                var repoA3 = new TenantCredentialSettingRepository(ctxA3);
                var list = await ctxA3.Set<TenantCredentialSetting>().ToListAsync(CancellationToken.None);
                Assert.Single(list);
                var fromA3 = await repoA3.GetByTenantAndCategoryAsync(tenantA, CredentialCategory.Model, CancellationToken.None);
                Assert.NotNull(fromA3);
                Assert.Equal("OpenAI", fromA3!.Provider);
                Assert.Equal("encA2", fromA3.EncryptedApiKey);
            }
        }
        finally
        {
            connection.Close();
        }
    }
}
