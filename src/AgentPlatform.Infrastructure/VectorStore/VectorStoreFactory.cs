using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Infrastructure.VectorStore;

/// <summary>
/// 依据当前部署配置解析并返回合适的 <see cref="IVectorStore"/> 实现：
/// 当数据库类型为 postgresql 且配置了 PostgreSQL 连接串与 OpenAI Key 时，
/// 使用 <see cref="PgVectorStore"/>（真实 pgvector 语义检索）；
/// 否则回退到 <see cref="InMemoryVectorStore"/>（进程内、确定性伪向量，供本地/测试/默认 SQLite 部署）。
/// </summary>
internal sealed class VectorStoreFactory : IVectorStoreFactory
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 初始化 <see cref="VectorStoreFactory"/> 的新实例。
    /// </summary>
    /// <param name="configuration">应用配置，用于读取数据库类型与连接串。</param>
    /// <param name="serviceProvider">用于惰性解析具体向量存储实现的依赖注入容器。</param>
    public VectorStoreFactory(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public IVectorStore Create()
    {
        var dbType = (_configuration["Database:Type"] ?? "sqlite").ToLowerInvariant();
        var pgConnection = _configuration.GetConnectionString("PostgreSQL");
        var openAiKey = _configuration["OpenAI:Key"];

        var usePostgres = dbType == "postgresql"
            && !string.IsNullOrEmpty(pgConnection)
            && !string.IsNullOrEmpty(openAiKey);

        return usePostgres
            ? _serviceProvider.GetRequiredService<PgVectorStore>()
            : _serviceProvider.GetRequiredService<InMemoryVectorStore>();
    }
}
