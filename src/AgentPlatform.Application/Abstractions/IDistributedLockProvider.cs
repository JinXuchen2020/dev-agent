namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 分布式锁抽象，用于多实例部署下防止定时调度重复触发同一工作流。
/// 实现：Redis（SET NX PX，完整分布式锁，S4 决策）或进程内回退（无 Redis 时本地/测试可用）。
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// 尝试获取一把带 TTL 的锁。返回 true 表示获取成功（调用方获得执行权）；false 表示被其他实例持有。
    /// 实现应在 Redis 不可用时降级为「放行」并告警，避免单实例环境下阻塞调度。
    /// </summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>释放锁（尽力而为；TTL 到期也会自动释放）。</summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);
}
