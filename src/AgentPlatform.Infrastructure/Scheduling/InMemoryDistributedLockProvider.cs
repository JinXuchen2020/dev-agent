using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Scheduling;

/// <summary>
/// 进程内分布式锁回退实现（无 Redis 时使用，单实例 / 本地 / 测试环境）。
/// 基于 ConcurrentDictionary + TTL 抢占；不跨进程，仅保证单进程内不重复触发。
/// </summary>
internal sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, DateTime> _locks = new();
    private readonly ILogger<InMemoryDistributedLockProvider> _logger;

    public InMemoryDistributedLockProvider(ILogger<InMemoryDistributedLockProvider> logger)
    {
        _logger = logger;
    }

    public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // 清理过期项（尽力而为，避免字典无限增长）。
        foreach (var kv in _locks)
        {
            if (kv.Value <= now && !_locks.TryRemove(kv.Key, out _))
                _ = 0;
        }

        var expiresAt = now + ttl;
        var acquired = _locks.TryAdd(key, expiresAt);
        if (acquired)
            _logger.LogDebug("进程内锁获取成功：{Key}（过期 {Expiry}）", key, expiresAt);
        return Task.FromResult(acquired);
    }

    public Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        _locks.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
