using System.Text.Json;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SyncJobService(AnimeDbContext db)
{
    public async Task<SyncJob?> QueueAsync(string jobType, string? parameters = null)
    {
        var alreadyActive = await db.SyncJobs.AnyAsync(j =>
            j.JobType == jobType && (j.Status == "Queued" || j.Status == "Running"));
        if (alreadyActive) return null;

        var job = new SyncJob { JobType = jobType, QueuedAt = DateTime.UtcNow, Parameters = parameters };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<SyncJob?> ContinueAsync(int failedJobId)
    {
        var failed = await db.SyncJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == failedJobId);
        if (failed == null || failed.Status != "Failed" || failed.ProcessedCount <= 0) return null;

        var alreadyActive = await db.SyncJobs.AnyAsync(j =>
            j.JobType == failed.JobType && (j.Status == "Queued" || j.Status == "Running"));
        if (alreadyActive) return null;

        // Jikan fetches from the API by season/year and is idempotent — re-run with same params.
        // AniList/Kitsu query the local DB ordered by Id, so we can skip already-processed records.
        var parameters = failed.JobType == "Jikan"
            ? failed.Parameters
            : JsonSerializer.Serialize(new { skipCount = failed.ProcessedCount });

        var job = new SyncJob { JobType = failed.JobType, QueuedAt = DateTime.UtcNow, Parameters = parameters };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<List<SyncJob>> GetRecentAsync(int count = 30)
        => await db.SyncJobs
            .OrderByDescending(j => j.QueuedAt)
            .Take(count)
            .ToListAsync();

    public async Task<SyncJob?> GetByIdAsync(int id)
        => await db.SyncJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);

    public async Task<List<SyncJobLog>> GetLogsAsync(int jobId)
        => await db.SyncJobLogs
            .AsNoTracking()
            .Where(l => l.SyncJobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ThenBy(l => l.Id)
            .ToListAsync();
}
