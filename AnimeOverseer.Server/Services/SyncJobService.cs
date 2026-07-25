using System.Text.Json;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SyncJobService(AnimeDbContext db, SyncJobTrigger syncJobTrigger)
{
    private static readonly string[] ImportSeasons = ["winter", "spring", "summer", "fall"];

    public async Task<SyncJob?> QueueAsync(string jobType, string? parameters = null)
    {
        var alreadyActive = await db.SyncJobs.AnyAsync(j =>
            j.JobType == jobType && (j.Status == "Queued" || j.Status == "Running"));
        if (alreadyActive) return null;

        var job = new SyncJob { JobType = jobType, QueuedAt = DateTime.UtcNow, Parameters = parameters };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        syncJobTrigger.Signal();
        return job;
    }

    public async Task<SyncJob?> ContinueAsync(int failedJobId)
    {
        var failed = await db.SyncJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == failedJobId);
        if (failed == null || failed.Status != "Failed" || failed.ProcessedCount <= 0) return null;

        var alreadyActive = await db.SyncJobs.AnyAsync(j =>
            j.JobType == failed.JobType && (j.Status == "Queued" || j.Status == "Running"));
        if (alreadyActive) return null;

        // Catalogue imports are idempotent when re-run with the same season parameters.
        // Legacy enrichment jobs query the local DB ordered by Id, so they can skip records.
        var parameters = failed.JobType is "Jikan" or "AniList" or "MyAnimeList" or "AnimeSchedule"
            ? failed.Parameters
            : JsonSerializer.Serialize(new { skipCount = failed.ProcessedCount });

        var job = new SyncJob { JobType = failed.JobType, QueuedAt = DateTime.UtcNow, Parameters = parameters };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        syncJobTrigger.Signal();
        return job;
    }

    /// <summary>
    /// Starts a historical catalogue import. A job contains only one season; completion
    /// queues the following season so imports remain serial and inherit API throttling.
    /// </summary>
    public async Task<SyncJob?> QueueInitialImportAsync(string source)
    {
        if (await db.SyncJobs.AnyAsync(j => j.JobType == source && (j.Status == "Queued" || j.Status == "Running"))) return null;
        return await QueueInitialImportPartAsync(source, 1960, ImportSeasons[0]);
    }

    public async Task QueueNextInitialImportAsync(SyncJob completedJob)
    {
        if (completedJob.Status != "Completed" || string.IsNullOrWhiteSpace(completedJob.Parameters)) return;
        try
        {
            using var document = JsonDocument.Parse(completedJob.Parameters);
            if (!document.RootElement.TryGetProperty("initialImport", out var initial) || !initial.GetBoolean()) return;
            var year = document.RootElement.GetProperty("years").EnumerateArray().Single().GetInt32();
            var season = document.RootElement.GetProperty("seasons").EnumerateArray().Single().GetString();
            var importSeasons = document.RootElement.TryGetProperty("initialImportSeasons", out var configuredSeasons)
                ? configuredSeasons.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray()
                : ImportSeasons;
            var seasonIndex = Array.IndexOf(importSeasons, season);
            if (seasonIndex < 0) return;
            var nextYear = seasonIndex == importSeasons.Length - 1 ? year + 1 : year;
            var nextSeason = importSeasons[(seasonIndex + 1) % importSeasons.Length];
            var endYear = document.RootElement.TryGetProperty("initialImportEndYear", out var configuredEndYear)
                ? configuredEndYear.GetInt32()
                : DateTime.UtcNow.Year + 1;
            if (nextYear > endYear) return;
            await QueueInitialImportPartAsync(completedJob.JobType, nextYear, nextSeason, endYear, importSeasons);
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
    }

    private async Task<SyncJob> QueueInitialImportPartAsync(string source, int year, string season, int? endYear = null, string[]? importSeasons = null)
    {
        var job = new SyncJob
        {
            JobType = source,
            QueuedAt = DateTime.UtcNow,
            Parameters = JsonSerializer.Serialize(new
            {
                initialImport = true,
                initialImportEndYear = endYear ?? DateTime.UtcNow.Year + 1,
                initialImportSeasons = importSeasons ?? ImportSeasons,
                years = new[] { year },
                seasons = new[] { season }
            })
        };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        syncJobTrigger.Signal();
        return job;
    }

    public async Task<bool> CancelAsync(int jobId)
    {
        var job = await db.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null || job.Status is not ("Queued" or "Running")) return false;

        if (job.Status == "Queued")
        {
            job.Status = "Cancelled";
            job.Message = "Cancelled before starting";
            job.FinishedAt = DateTime.UtcNow;
        }
        else
        {
            job.CancellationRequested = true;
            job.Message = "Cancellation requested";
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<SyncJob>> GetRecentAsync(int skip = 0, int count = 30)
        => await db.SyncJobs
            .AsNoTracking()
            .OrderByDescending(j => j.QueuedAt)
            .Skip(skip)
            .Take(count)
            .ToListAsync();

    public Task<int> GetCountAsync()
        => db.SyncJobs.CountAsync();

    public async Task<SyncJob?> GetByIdAsync(int id)
        => await db.SyncJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);

    public async Task<List<SyncJobLog>> GetLogsAsync(int jobId)
        => await db.SyncJobLogs
            .AsNoTracking()
            .Where(l => l.SyncJobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ThenBy(l => l.Id)
            .ToListAsync();

    public async Task<List<Anime>> GetSyncedAnimesAsync(SyncJob job)
    {
        if (string.IsNullOrWhiteSpace(job.SyncedAnimeIds)) return [];

        List<int>? ids;
        try { ids = JsonSerializer.Deserialize<List<int>>(job.SyncedAnimeIds); }
        catch (JsonException) { return []; }
        if (ids is not { Count: > 0 }) return [];

        var order = ids.Select((id, index) => new { id, index })
            .GroupBy(x => x.id)
            .ToDictionary(group => group.Key, group => group.First().index);
        var animes = await db.Animes.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();

        // Dictionary lookups are not translatable to SQL, so preserve the job's
        // recorded sync order only after EF has fetched the matching rows.
        return animes.OrderBy(a => order[a.Id]).ToList();
    }
}
