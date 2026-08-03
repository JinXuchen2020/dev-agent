namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 计算 Schedule 触发器下一次运行时间（考虑 cron 表达式与目标时区）。
/// 抽象以便测试与替换实现。
/// </summary>
public interface IScheduleCalculator
{
    /// <summary>
    /// 基于 <paramref name="fromUtc"/>（UTC）计算下一个满足 cron 表达式的 UTC 时刻。
    /// </summary>
    /// <param name="cronExpression">标准 5 段 cron 表达式（分 时 日 月 周）。</param>
    /// <param name="timeZoneId">IANA 时区标识（如 "UTC"、"Asia/Shanghai"）。</param>
    /// <param name="fromUtc">基准 UTC 时间（含），从该时刻之后寻找下一次。</param>
    /// <returns>下一次运行的 UTC 时间；表达式非法或在该时区无解时返回 null。</returns>
    DateTime? ComputeNextRunUtc(string cronExpression, string timeZoneId, DateTime fromUtc);
}
