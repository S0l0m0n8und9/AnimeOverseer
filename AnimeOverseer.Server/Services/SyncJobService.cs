using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SyncJobService(AnimeDbContext db)
{
    public async Task<SyncJob?> QueueAsync(string jobType)
    {
        var alreadyActive = await db.SyncJobs.AnyAsync(j =>
            j.JobType == jobType && (j.Status == "Queued" || j.Status == "Running"));
        if (alreadyActive) return null;

        var job = new SyncJob { JobType = jobType, QueuedAt = DateTime.UtcNow };
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
        => await db.SyncJobs.FindAsync(id);

    public async Task<List<SyncJobLog>> GetLogsAsync(int jobId)
        => await db.SyncJobLogs
            .Where(l => l.SyncJobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ThenBy(l => l.Id)
            .ToListAsync();
}
