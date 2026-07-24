using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AnimeOverseer.Server.BackgroundServices;

public class NightlySyncService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5); // Delay startup to let other services initialize

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncJobTrigger _syncJobTrigger;
    private readonly ILogger<NightlySyncService> _logger;

    public NightlySyncService(IServiceScopeFactory scopeFactory, SyncJobTrigger syncJobTrigger, ILogger<NightlySyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _syncJobTrigger = syncJobTrigger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformNightlySyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nightly sync failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task PerformNightlySyncAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nightly sync: starting full anime synchronization");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();

        // Create a sync job for the AniList catalogue (all years and seasons).
        var job = new SyncJob
        {
            JobType = "AniList",
            Status = "Queued",
            QueuedAt = DateTime.UtcNow,
            Parameters = JsonSerializer.Serialize(new { years = new int[] { DateTime.UtcNow.Year, DateTime.UtcNow.Year + 1, DateTime.UtcNow.Year + 2, DateTime.UtcNow.Year + 3 }, seasons = new[] { "spring", "summer", "fall", "winter" } })
        };

        db.SyncJobs.Add(job);
        await db.SaveChangesAsync(stoppingToken);
        _syncJobTrigger.Signal();

        _logger.LogInformation("Nightly sync: queued AniList sync job {JobId}", job.Id);

        // Wait for job to complete (with timeout)
        var completed = await WaitForJobCompletionAsync(db, job.Id, stoppingToken, TimeSpan.FromHours(6));

        if (completed)
        {
            _logger.LogInformation("Nightly sync: completed successfully");
        }
        else
        {
            _logger.LogWarning("Nightly sync: timed out or was cancelled");
        }
    }

    private async Task<bool> WaitForJobCompletionAsync(AnimeDbContext db, int jobId, CancellationToken stoppingToken, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested && (DateTime.UtcNow - startTime) < timeout)
        {
            var job = await db.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, stoppingToken);

            if (job == null)
            {
                _logger.LogWarning("Nightly sync: job {JobId} not found", jobId);
                return false;
            }

            if (job.Status == "Completed" || job.Status == "Failed")
            {
                _logger.LogInformation("Nightly sync: job {JobId} finished with status {Status}", job.Id, job.Status);
                return job.Status == "Completed";
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        return false;
    }
}
