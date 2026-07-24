using System.Text.Json;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SyncService(AnimeDbContext db, IServiceScopeFactory scopeFactory, AniListApiService aniList, KitsuApiService kitsu)
{
    public async Task RunJikanSyncAsync(SyncJob job, CancellationToken ct)
    {
        var (years, seasons) = ParseJikanParams(job.Parameters);
        // TotalCount is 0 because total anime across all pages is unknown upfront.
        job.TotalCount = 0;
        Log(job, $"AniList catalogue sync started — {seasons.Length} season(s) × {years.Length} year(s)");
        await db.SaveChangesAsync(ct);

        foreach (var year in years)
        {
            if (ct.IsCancellationRequested) break;

            Log(job, $"Fetching {string.Join(", ", seasons)} {year}…");
            await db.SaveChangesAsync(ct);

            // Callback fires after every page; persists the running anime count immediately.
            // Uses SyncService's own db context — AnimeCacheService has its own isolated context.
            async Task OnPageFetched(int page, int count)
            {
                ct.ThrowIfCancellationRequested();
                job.ProcessedCount += count;
                db.SyncJobs.Update(job);
                db.Entry(job).Property(j => j.CancellationRequested).IsModified = false;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            // Resolve AnimeCacheService in its own child scope so it gets a dedicated DbContext,
            // preventing ChangeTracker conflicts with this service's db instance.
            using var cacheScope = scopeFactory.CreateScope();
            var cache = cacheScope.ServiceProvider.GetRequiredService<AnimeCacheService>();
            await cache.FetchAndCacheSeasonsAsync(year, seasons, OnPageFetched, ct);

            if (ct.IsCancellationRequested) break;

            // AnimeCacheService has already committed all anime data; clear any residual
            // tracked entities so only the job update and log entry are saved here.
            db.ChangeTracker.Clear();
            db.SyncJobs.Update(job);
            db.Entry(job).Property(j => j.CancellationRequested).IsModified = false;
            Log(job, $"Completed {year} — {job.ProcessedCount:N0} anime fetched so far", "Success");
            await db.SaveChangesAsync(ct);
        }

        if (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            db.SyncJobs.Update(job);
            db.Entry(job).Property(j => j.CancellationRequested).IsModified = false;
            Log(job, $"Sync complete — {job.ProcessedCount:N0} anime processed", "Success");
            await db.SaveChangesAsync(ct);
        }
    }

    private static int ParseSkipCount(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters)) return 0;
        try
        {
            var doc = JsonDocument.Parse(parameters);
            if (doc.RootElement.TryGetProperty("skipCount", out var el))
                return el.GetInt32();
        }
        catch { }
        return 0;
    }

    private static (int[] Years, string[] Seasons) ParseJikanParams(string? parameters)
    {
        if (!string.IsNullOrWhiteSpace(parameters))
        {
            try
            {
                var doc = JsonDocument.Parse(parameters);
                var years = doc.RootElement.GetProperty("years")
                    .EnumerateArray().Select(e => e.GetInt32()).ToArray();
                var seasons = doc.RootElement.GetProperty("seasons")
                    .EnumerateArray().Select(e => e.GetString()!).ToArray();
                if (years.Length > 0 && seasons.Length > 0)
                    return (years, seasons);
            }
            catch { }
        }
        return ([DateTime.UtcNow.Year], ["spring", "summer", "fall", "winter"]);
    }

    public async Task RunAniListSyncAsync(SyncJob job, CancellationToken ct)
    {
        int skipCount = ParseSkipCount(job.Parameters);
        var animes = await db.Animes
            .Where(a => a.MALId != null && a.MALId > 0)
            .OrderBy(a => a.Id)
            .Skip(skipCount)
            .ToListAsync(ct);

        job.TotalCount = animes.Count + skipCount;
        job.ProcessedCount = skipCount;
        var resumeNote = skipCount > 0 ? $" (resuming from {skipCount:N0})" : "";
        Log(job, $"Starting AniList enrichment — {job.TotalCount:N0} anime with MAL IDs{resumeNote}");
        await db.SaveChangesAsync(ct);

        int errors = 0;
        foreach (var anime in animes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var data = await aniList.GetByMalIdAsync(anime.MALId!.Value);
                if (data != null) EnrichFields(anime, data);
            }
            catch { errors++; }

            job.ProcessedCount++;

            if (job.ProcessedCount % 100 == 0)
                Log(job, $"Progress: {job.ProcessedCount:N0} / {job.TotalCount:N0}{(errors > 0 ? $" ({errors} errors)" : "")}");

            if (job.ProcessedCount % 20 == 0)
                await db.SaveChangesAsync(ct);

            await Task.Delay(700, ct); // ~85 req/min, under AniList rate limit
        }

        await db.SaveChangesAsync(ct);

        var summary = errors > 0
            ? $"Sync complete — {job.ProcessedCount:N0} processed, {errors} errors"
            : $"Sync complete — {job.ProcessedCount:N0} processed";
        Log(job, summary, errors > 0 ? "Warning" : "Success");
        await db.SaveChangesAsync(ct);
    }

    public async Task RunKitsuSyncAsync(SyncJob job, CancellationToken ct)
    {
        int skipCount = ParseSkipCount(job.Parameters);
        var animes = await db.Animes
            .Where(a => (a.KitsuId == null || a.KitsuId == 0) && a.Title != null)
            .OrderBy(a => a.Id)
            .Skip(skipCount)
            .ToListAsync(ct);

        job.TotalCount = animes.Count + skipCount;
        job.ProcessedCount = skipCount;
        var resumeNote = skipCount > 0 ? $" (resuming from {skipCount:N0})" : "";
        Log(job, $"Starting Kitsu enrichment — {job.TotalCount:N0} anime without Kitsu data{resumeNote}");
        await db.SaveChangesAsync(ct);

        int errors = 0;
        foreach (var anime in animes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var data = await kitsu.GetByTitleAsync(anime.Title);
                if (data != null) EnrichFields(anime, data);
            }
            catch { errors++; }

            job.ProcessedCount++;

            if (job.ProcessedCount % 100 == 0)
                Log(job, $"Progress: {job.ProcessedCount:N0} / {job.TotalCount:N0}{(errors > 0 ? $" ({errors} errors)" : "")}");

            if (job.ProcessedCount % 50 == 0)
                await db.SaveChangesAsync(ct);

            await Task.Delay(200, ct);
        }

        await db.SaveChangesAsync(ct);

        var summary = errors > 0
            ? $"Sync complete — {job.ProcessedCount:N0} processed, {errors} errors"
            : $"Sync complete — {job.ProcessedCount:N0} processed";
        Log(job, summary, errors > 0 ? "Warning" : "Success");
        await db.SaveChangesAsync(ct);
    }

    // Queues a log entry to be saved with the next SaveChangesAsync call.
    private void Log(SyncJob job, string message, string level = "Info")
    {
        db.SyncJobLogs.Add(new SyncJobLog
        {
            SyncJobId = job.Id,
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message
        });
    }

    private static void EnrichFields(Anime target, Anime source)
    {
        if (!target.HasEnglishTitle && source.HasEnglishTitle)
        {
            target.Title = source.Title;
            target.HasEnglishTitle = true;
        }
        if (string.IsNullOrWhiteSpace(target.Synopsis) && !string.IsNullOrWhiteSpace(source.Synopsis))
            target.Synopsis = source.Synopsis;
        if (target.Episodes == null && source.Episodes != null)
            target.Episodes = source.Episodes;
        if (target.Rating == null && source.Rating != null)
            target.Rating = source.Rating;
        if (string.IsNullOrWhiteSpace(target.OriginalTitle) && !string.IsNullOrWhiteSpace(source.OriginalTitle))
            target.OriginalTitle = source.OriginalTitle;
        if ((target.AniListId == null || target.AniListId == 0) && source.AniListId is > 0)
            target.AniListId = source.AniListId;
        if ((target.KitsuId == null || target.KitsuId == 0) && source.KitsuId is > 0)
            target.KitsuId = source.KitsuId;
    }
}
