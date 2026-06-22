using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.BackgroundServices;

public class SyncJobRunnerService(IServiceScopeFactory scopeFactory, ILogger<SyncJobRunnerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResetStaleJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        { 
            var millisecondsDelay = 3 * 60 * 1000;
            await Task.Delay(millisecondsDelay, stoppingToken);
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Sync job runner error"); }
        }
    }

    // Mark any jobs left in Running state from a previous run as Failed.
    private async Task ResetStaleJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var stale = await db.SyncJobs
            .Where(j => j.Status == "Running")
            .ToListAsync(ct);

        foreach (var job in stale)
        {
            job.Status = "Failed";
            job.Message = "Interrupted by server restart";
            job.FinishedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var job = await db.SyncJobs
            .Where(j => j.Status == "Queued")
            .OrderBy(j => j.QueuedAt)
            .FirstOrDefaultAsync(ct);

        if (job == null) return;

        job.Status = "Running";
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Starting sync job {Id} ({Type})", job.Id, job.JobType);

        var sync = scope.ServiceProvider.GetRequiredService<SyncService>();

        try
        {
            await (job.JobType switch
            {
                "Jikan"   => sync.RunJikanSyncAsync(job, ct),
                "AniList" => sync.RunAniListSyncAsync(job, ct),
                "Kitsu"   => sync.RunKitsuSyncAsync(job, ct),
                _         => Task.CompletedTask
            });

            job.Status = ct.IsCancellationRequested ? "Failed" : "Completed";
            job.Message = ct.IsCancellationRequested
                ? "Cancelled"
                : $"Processed {job.ProcessedCount} of {job.TotalCount}";
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Message = ex.Message;
            logger.LogError(ex, "Sync job {Id} ({Type}) failed", job.Id, job.JobType);
        }
        finally
        {
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None); // always persist final status
        }

        logger.LogInformation("Sync job {Id} ({Type}) finished with status {Status}", job.Id, job.JobType, job.Status);
    }
}
