using AgentPlatform.Application.Abstractions;
using Cronos;

namespace AgentPlatform.Infrastructure.Scheduling;

/// <summary>
/// 基于 Cronos 的 cron 调度计算（支持 IANA 时区与 DST）。
/// cron 表达式为标准 5 段（分 时 日 月 周）；非法表达式返回 null。
/// </summary>
internal sealed class CronCalculator : IScheduleCalculator
{
    public DateTime? ComputeNextRunUtc(string cronExpression, string timeZoneId, DateTime fromUtc)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return null;

        if (!CronExpression.TryParse(cronExpression, out var expression))
            return null;

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        var from = new DateTimeOffset(fromUtc);
        var next = expression.GetNextOccurrence(from, zone, inclusive: false);
        return next?.UtcDateTime;
    }
}
