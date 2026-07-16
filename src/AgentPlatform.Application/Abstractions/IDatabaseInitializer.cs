namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 定义数据库初始化服务的契约，负责数据库迁移、表创建和种子数据初始化。
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// 异步初始化数据库，运行迁移并创建表结构，必要时填充种子数据。
    /// </summary>
    /// <param name="ct">A token to observe for cancellation of the initialization request.</param>
    Task InitializeAsync(CancellationToken ct = default);
}
