using AgentPlatform.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Jobs;

/// <summary>
/// Background service that periodically purges execution logs older than the configured retention period.
/// Runs every 24 hours by default, checking for logs whose <c>StartedAt</c> exceeds <c>RetentionDays</c>.
/// </summary>
internal sealed class ExecutionLogCleanupJob : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ExecutionLogSettings> _settings;
    private readonly ILogger<ExecutionLogCleanupJob> _logger;
    private readonly TimeSpan _checkInterval;

    public ExecutionLogCleanupJob(
        IServiceScopeFactory scopeFactory,
        IOptions<ExecutionLogSettings> settings,
        ILogger<ExecutionLogCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
        _checkInterval = TimeSpan.FromHours(Math.Max(1, settings.Value.CheckIntervalHours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Execution log cleanup job started (retention: {RetentionDays} days, check interval: {Interval}h)",
            _settings.Value.RetentionDays, _checkInterval.TotalHours);

        // Delay initial run by 5 minutes to let the app stabilize
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during execution log cleanup");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_settings.Value.RetentionDays);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Persistence.AppDbContext>();

        // Count first
        var count = await context.Set<Domain.Aggregates.ExecutionLogs.ExecutionLog>()
            .Where(l => l.StartedAt < cutoff)
            .LongCountAsync(ct);

        if (count == 0)
        {
            _logger.LogDebug("No execution logs older than {Cutoff} found for cleanup", cutoff);
            return;
        }

        const int batchSize = 500;
        var deleted = 0L;

        while (deleted < count)
        {
            var batch = await context.Set<Domain.Aggregates.ExecutionLogs.ExecutionLog>()
                .Where(l => l.StartedAt < cutoff)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            context.Set<Domain.Aggregates.ExecutionLogs.ExecutionLog>().RemoveRange(batch);
            await context.SaveChangesAsync(ct);
            deleted += batch.Count;

            _logger.LogInformation(
                "Cleaned up batch of {BatchSize} execution log(s) (total: {Deleted}/{Total})",
                batch.Count, deleted, count);
        }

        _logger.LogInformation(
            "Cleaned up {Count} execution log(s) older than {Cutoff} (retention: {RetentionDays} days)",
            count, cutoff, _settings.Value.RetentionDays);
    }
}
