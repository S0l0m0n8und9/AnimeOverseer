using AnimeOverseer.Server.Services;

namespace AnimeOverseer.Server.BackgroundServices;

/// <summary>Checks persisted source schedules; all actual API work remains in the serial job runner.</summary>
public sealed class IntegrationScheduleServiceWorker(IServiceScopeFactory scopeFactory, ILogger<IntegrationScheduleServiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IntegrationScheduleService>().QueueDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Scheduled integration check failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
