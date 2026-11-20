using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Services;

public sealed class ActivityLogRetentionOptions
{
    public int RetentionDays { get; set; } = 1825;
    public int SweepIntervalHours { get; set; } = 24;
}

public sealed class ActivityLogRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<ActivityLogRetentionOptions> options,
    ILogger<ActivityLogRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Clamp(options.Value.SweepIntervalHours, 1, 168));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ActivityLogService>();
                var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(options.Value.RetentionDays, 30, 3650));
                var deleted = await service.DeleteExpiredAsync(cutoff, stoppingToken);
                if (deleted.UserLogsDeleted + deleted.SecurityLogsDeleted > 0)
                    logger.LogInformation("Activity retention removed {UserLogs} user events and {SecurityLogs} security events older than {CutoffUtc}.",
                        deleted.UserLogsDeleted, deleted.SecurityLogsDeleted, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Activity retention sweep failed with exception type {ExceptionType}.", exception.GetType().Name);
            }
        }
    }
}
