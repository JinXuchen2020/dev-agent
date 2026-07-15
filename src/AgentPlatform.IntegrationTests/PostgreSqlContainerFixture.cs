using Testcontainers.PostgreSql;

namespace AgentPlatform.IntegrationTests;

/// <summary>
/// Shared PostgreSQL Testcontainer fixture for integration tests.
/// Starts a real PostgreSQL container before test collection execution
/// and stops it after all tests complete.
///
/// Phase 2+ usage: Inject this fixture into test classes that need
/// a real database to verify EF Core queries, migrations, and data access.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("agent_platform_test")
        .WithUsername("test")
        .WithPassword("test_password")
        .WithCleanUp(true)
        .Build();

    /// <summary>
    /// Connection string to the running PostgreSQL container.
    /// Use this to configure EF Core DbContext in test WebApplicationFactory.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Collection definition for tests that share the PostgreSQL container.
/// Usage: [CollectionDefinition("PostgreSqlContainer")]
///        [Collection("PostgreSqlContainer")]
/// </summary>
[CollectionDefinition("PostgreSqlContainer")]
public sealed class PostgreSqlContainerCollection : ICollectionFixture<PostgreSqlContainerFixture>;
