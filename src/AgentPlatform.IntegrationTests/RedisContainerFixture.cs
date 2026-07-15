using Testcontainers.Redis;

namespace AgentPlatform.IntegrationTests;

/// <summary>
/// Shared Redis Testcontainer fixture for integration tests.
/// Starts a real Redis container before test collection execution
/// and stops it after all tests complete.
///
/// Phase 2+ usage: Test IShortTermMemory implementations against real Redis.
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    /// <summary>
    /// Connection string to the running Redis container.
    /// Use to configure StackExchange.Redis ConnectionMultiplexer in tests.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("RedisContainer")]
public sealed class RedisContainerCollection : ICollectionFixture<RedisContainerFixture>;
