using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentPlatform.Infrastructure.Scheduling;

/// <summary>
/// 基于 Redis 的分布式锁（SET NX PX）。多实例部署下防止定时调度重复触发同一工作流。
/// 释放采用令牌 CAS（仅当值匹配本实例持有令牌时删除），避免 TTL 过期后被他实例抢占、
/// 本实例释放时误删他实例锁的经典竞态。Redis 不可用时降级为「放行」并告警，避免单实例
/// 环境下阻塞调度（与全局 Redis 降级策略一致）；降级路径不持有真实锁，释放时直接跳过。
/// </summary>
internal sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    // SET key token NX PX(ttl) —— 仅当 key 不存在时设置，天然互斥；value 为本实例唯一令牌。
    private const string AcquireScript =
        "if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then return 1 else return 0 end";

    // 仅当 key 的当前值等于本实例令牌时才删除（释放自己持有的锁）。
    private const string ReleaseScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    // 进程内记录本实例持有哪些锁及其令牌，供释放时做 CAS（不跨实例）。
    private readonly ConcurrentDictionary<string, string> _heldTokens = new();

    public RedisDistributedLockProvider(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisDistributedLockProvider> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        try
        {
            var db = _multiplexer.GetDatabase();
            var acquired = (long)await db.ScriptEvaluateAsync(
                AcquireScript,
                new RedisKey[] { key },
                new RedisValue[] { token, (long)ttl.TotalMilliseconds });
            if (acquired == 1)
            {
                _heldTokens[key] = token;
                return true;
            }

            return false;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis 锁获取失败（降级放行）：{Key}", key);
            return true; // 降级：不记录令牌，释放时跳过。
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis 锁获取超时（降级放行）：{Key}", key);
            return true;
        }
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        // 降级获取路径未持有真实锁：跳过释放，避免误删他实例锁。
        if (!_heldTokens.TryRemove(key, out var token))
            return;

        try
        {
            var db = _multiplexer.GetDatabase();
            await db.ScriptEvaluateAsync(
                ReleaseScript,
                new RedisKey[] { key },
                new RedisValue[] { token });
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis 锁释放失败（忽略）：{Key}", key);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis 锁释放超时（忽略）：{Key}", key);
        }
    }
}
