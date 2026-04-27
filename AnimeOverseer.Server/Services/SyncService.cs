using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SyncService(AnimeDbContext db, IAnimeDataSource dataSource, AniListApiService aniList, KitsuApiService kitsu)
{
    private static readonly string[] Seasons = ["spring", "summer", "fall", "winter"];

    // Triggers the existing season sync pipeline (Jikan fetch + AniList enrichment) for the current year.
    public async Task RunJikanSyncAsync(SyncJob job, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        job.TotalCount = Seasons.Length;
        await db.SaveChangesAsync(ct);

        foreach (var season in Seasons)
        {
            if (ct.IsCancellationRequested) break;
            await dataSource.GetSeasonAnimes(year, season, forceRefresh: true);
            job.ProcessedCount++;
            await db.SaveChangesAsync(ct);
        }
    }

    // Re-enriches all DB anime that have a MAL ID by fetching fresh data from AniList.
    public async Task RunAniListSyncAsync(SyncJob job, CancellationToken ct)
    {
        var animes = await db.Animes
            .Where(a => a.MALId != null && a.MALId > 0)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        job.TotalCount = animes.Count;
        await db.SaveChangesAsync(ct);

        foreach (var anime in animes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var data = await aniList.GetByMalIdAsync(anime.MALId!.Value);
                if (data != null) EnrichFields(anime, data);
            }
            catch { }

            job.ProcessedCount++;
            if (job.ProcessedCount % 20 == 0)
                await db.SaveChangesAsync(ct);

            await Task.Delay(700, ct); // ~85 req/min, under AniList rate limit
        }

        await db.SaveChangesAsync(ct);
    }

    // Fills in missing Kitsu data for anime that don't yet have a KitsuId.
    public async Task RunKitsuSyncAsync(SyncJob job, CancellationToken ct)
    {
        var animes = await db.Animes
            .Where(a => (a.KitsuId == null || a.KitsuId == 0) && a.Title != null)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        job.TotalCount = animes.Count;
        await db.SaveChangesAsync(ct);

        foreach (var anime in animes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var data = await kitsu.GetByTitleAsync(anime.Title);
                if (data != null) EnrichFields(anime, data);
            }
            catch { }

            job.ProcessedCount++;
            if (job.ProcessedCount % 50 == 0)
                await db.SaveChangesAsync(ct);

            await Task.Delay(200, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void EnrichFields(Anime target, Anime source)
    {
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
