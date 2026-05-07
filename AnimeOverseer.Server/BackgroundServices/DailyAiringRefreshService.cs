using AnimeOverseer.Server.Services;

namespace AnimeOverseer.Server.BackgroundServices;

public class DailyAiringRefreshService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyAiringRefreshService> _logger;

    public DailyAiringRefreshService(IServiceScopeFactory scopeFactory, ILogger<DailyAiringRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAiringAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily airing refresh failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task RefreshAiringAsync()
    {
        _logger.LogInformation("Daily airing refresh: fetching currently-airing anime from /seasons/now");

        using var scope = _scopeFactory.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<AnimeCacheService>();

        await cacheService.FetchAndCacheCurrentlyAiringAsync();
        _logger.LogInformation("Daily airing refresh complete");
    }
}
