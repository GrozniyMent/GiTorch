using gitorch.Configuration;
using gitorch.Services.Abstractions;

namespace gitorch.Services;

public sealed class RetentionCleanupService(
    ISessionRepository repository,
    OrchestratorConfig config,
    ILogger<RetentionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.RetentionCleanupIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        RunCleanup();

        while (await timer.WaitForNextTickAsync(stoppingToken))
            RunCleanup();
    }

    private void RunCleanup()
    {
        try
        {
            var deleted = repository.PurgeExpiredRecords(
                TimeSpan.FromDays(config.DeliveryRetentionDays),
                TimeSpan.FromDays(config.SessionRetentionDays));

            if (deleted > 0)
                logger.LogInformation("Retention cleanup purged {Count} expired database records.", deleted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Retention cleanup failed.");
        }
    }
}

