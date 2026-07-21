using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Jobs;

/// <summary>
/// Background service that periodically scans for API keys nearing expiration
/// and emits alert entries. Runs every 6 hours by default, checking for keys
/// expiring within the configured threshold (default 7 days).
/// </summary>
internal sealed class ApiKeyExpiryJob : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiKeyExpiryJob> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly int _expiryWarningDays;

    private const int DefaultExpiryWarningDays = 7;
    private const int DefaultCheckIntervalHours = 6;

    public ApiKeyExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ApiKeyExpiryJob> logger,
        IOptions<ApiKeyExpirySettings>? settings = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var s = settings?.Value ?? new ApiKeyExpirySettings();
        _expiryWarningDays = s.ExpiryWarningDays > 0 ? s.ExpiryWarningDays : DefaultExpiryWarningDays;
        _checkInterval = TimeSpan.FromHours(s.CheckIntervalHours > 0 ? s.CheckIntervalHours : DefaultCheckIntervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "API key expiry job started (warning: {Days}d ahead, check interval: {Interval}h)",
            _expiryWarningDays, _checkInterval.TotalHours);

        // Delay initial run by 10 minutes to let the app stabilize
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiringKeysAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during API key expiry check");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckExpiringKeysAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

        // 1) Revoke keys that are already past expiry but still active, so they can
        //    no longer authenticate. This is the live call site for ApiKey.Revoke().
        var expiredKeys = await repository.GetExpiredActiveKeysAsync(ct);
        if (expiredKeys.Count > 0)
        {
            var rotationService = scope.ServiceProvider.GetRequiredService<IKeyRotationService>();
            foreach (var expired in expiredKeys)
            {
                _logger.LogWarning(
                    "API key {KeyPrefix} (ID: {KeyId}, tenant: {TenantId}) expired on {ExpiresAt:yyyy-MM-dd} — revoking",
                    expired.KeyPrefix, expired.Id, expired.TenantId, expired.ExpiresAt);
                try
                {
                    await rotationService.RevokeKeyAsync(expired.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to revoke expired API key {KeyId}", expired.Id);
                }
            }
        }

        // 2) Warn about (and auto-rotate) keys approaching expiry.
        var expiringKeys = await repository.GetExpiringKeysAsync(_expiryWarningDays, ct);

        if (expiringKeys.Count == 0)
        {
            _logger.LogDebug("No API keys expiring within {Days} days", _expiryWarningDays);
            return;
        }

        foreach (var key in expiringKeys)
        {
            _logger.LogWarning(
                "API key {KeyPrefix} (ID: {KeyId}, tenant: {TenantId}) expires on {ExpiresAt:yyyy-MM-dd} " +
                "(within {Days}d warning threshold)",
                key.KeyPrefix, key.Id, key.TenantId, key.ExpiresAt, _expiryWarningDays);

            // Auto-rotate once before expiry. We guard on KeyVersion == 1 so the key is
            // rotated a single time as it approaches expiry rather than on every 6h cycle
            // (rotation advances the version but intentionally does not extend ExpiresAt here).
            if (key.KeyVersion == 1)
            {
                try
                {
                    var rotationService = scope.ServiceProvider.GetRequiredService<IKeyRotationService>();
                    await rotationService.RotateKeyAsync(key.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-rotate expiring API key {KeyId}", key.Id);
                }
            }
        }
    }
}

/// <summary>
/// Settings for the API key expiry background job.
/// </summary>
public sealed class ApiKeyExpirySettings
{
    /// <summary>Number of days before expiry to start warning (default: 7).</summary>
    public int ExpiryWarningDays { get; set; } = 7;

    /// <summary>Check interval in hours (default: 6).</summary>
    public int CheckIntervalHours { get; set; } = 6;
}
