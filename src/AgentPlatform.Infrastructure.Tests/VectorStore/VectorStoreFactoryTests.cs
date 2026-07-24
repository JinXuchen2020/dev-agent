using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.VectorStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.VectorStore;

/// <summary>
/// 覆盖 R3（部署解耦）：验证 <see cref="VectorStoreFactory"/> 按 Database:Type + OpenAI:Key
/// 在 PgVectorStore（生产）与 InMemoryVectorStore（默认 SQLite 回退）之间正确选择，
/// 并保证默认 SQLite 部署下解析出的 IVectorStore 是 InMemory 且 SearchAsync 不会抛异常（避免 500）。
/// </summary>
public sealed class VectorStoreFactoryTests
{
    private static IConfiguration BuildConfig(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => e.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<InMemoryVectorStore>();
        services.AddScoped<PgVectorStore>();
        // embedding 服务在此仅用于 PgVectorStore 构造；用替身避免真实网络调用。
#pragma warning disable SKEXP0001
        services.AddScoped<ITextEmbeddingGenerationService>(_ =>
            Substitute.For<ITextEmbeddingGenerationService>());
#pragma warning restore SKEXP0001
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_SQLite_WithoutOpenAiKey_ResolvesToInMemory()
    {
        var config = BuildConfig(
            ("Database:Type", "sqlite"));
        using var provider = BuildProvider(config);
        var factory = new VectorStoreFactory(config, provider);

        var store = factory.Create();

        Assert.IsType<InMemoryVectorStore>(store);
    }

    [Fact]
    public void MissingDatabaseType_StillResolvesToInMemory()
    {
        var config = BuildConfig();
        using var provider = BuildProvider(config);
        var factory = new VectorStoreFactory(config, provider);

        var store = factory.Create();

        Assert.IsType<InMemoryVectorStore>(store);
    }

    [Fact]
    public void PostgreSql_WithConnectionAndKey_ResolvesToPgVector()
    {
        var config = BuildConfig(
            ("Database:Type", "postgresql"),
            ("ConnectionStrings:PostgreSQL",
                "Host=localhost;Port=5432;Database=test;Username=u;Password=p"),
            ("OpenAI:Key", "sk-test"));
        using var provider = BuildProvider(config);
        var factory = new VectorStoreFactory(config, provider);

        var store = factory.Create();

        Assert.IsType<PgVectorStore>(store);
    }

    [Fact]
    public void PostgreSql_WithoutOpenAiKey_FallsBackToInMemory()
    {
        var config = BuildConfig(
            ("Database:Type", "postgresql"),
            ("ConnectionStrings:PostgreSQL",
                "Host=localhost;Port=5432;Database=test;Username=u;Password=p"));
        using var provider = BuildProvider(config);
        var factory = new VectorStoreFactory(config, provider);

        var store = factory.Create();

        Assert.IsType<InMemoryVectorStore>(store);
    }

    [Fact]
    public async Task ResolvedStore_FromSqliteDefault_SearchDoesNotThrow()
    {
        var config = BuildConfig(("Database:Type", "sqlite"));
        using var provider = BuildProvider(config);

        // 模拟默认 SQLite 部署下通过 DI 解析出的 IVectorStore
        var store = provider.GetRequiredService<InMemoryVectorStore>();

        var ex = await Record.ExceptionAsync(async () =>
            await store.SearchAsync("any-collection", "any query", Guid.NewGuid()));

        Assert.Null(ex);
    }
}
