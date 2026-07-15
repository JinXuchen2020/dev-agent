using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AgentPlatform.Infrastructure.Providers;

/// <summary>
/// SQLite 数据库提供程序
/// 适用于开发、测试和小型生产环境
/// </summary>
public sealed class SqliteDatabaseProvider
{
    private readonly IConfiguration _configuration;

    public SqliteDatabaseProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string DbType => "sqlite";

    public string GetConnectionString()
    {
        // 优先从 appsettings 读取，否则使用默认值
        var connection = _configuration["ConnectionStrings:DefaultConnection"]
            ?? "Data Source=agent_platform.db;Cache=Shared";

        return connection;
    }

    public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(GetConnectionString());
    }

    public string GetInitializationSql()
    {
        // SQLite 不需要特殊初始化 SQL
        return string.Empty;
    }
}
