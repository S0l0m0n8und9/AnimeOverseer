using AnimeOverseer.Server.Services;

namespace AnimeOverseer.Server.BackgroundServices;

public class AnimeRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnimeRefreshBackgroundService> _logger;

    public AnimeRefreshBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AnimeRefreshBackgroundService> logger)
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
                await RefreshCurrentYearAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background anime refresh failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task RefreshCurrentYearAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        _logger.LogInformation("Background refresh: checking cache for {Year}", year);

        using var scope = _scopeFactory.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<IAnimeDataSource>();

        // GetSeasonAnimes fetches all seasons for the year; it returns fast if cache is still fresh
        var animes = await dataSource.GetSeasonAnimes(year, "spring");
        _logger.LogInformation("Background refresh complete: {Count} anime for {Year}", animes.Count, year);
    }
}
