using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class AnimeCacheService : IAnimeDataSource
{
    private static readonly string[] AllSeasons = ["spring", "summer", "fall", "winter"];
    private static readonly TimeSpan SeasonCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromHours(24);

    private readonly AnimeDbContext _db;
    private readonly JikanApiService _jikan;
    private readonly ImageCacheService _imageCache;

    public AnimeCacheService(AnimeDbContext db, JikanApiService jikan, ImageCacheService imageCache)
    {
        _db = db;
        _jikan = jikan;
        _imageCache = imageCache;
    }

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = await LoadCachedYear(year);
            if (cached.Count > 0) return cached;
        }
        return await FetchAndCacheYear(year);
    }

    public Task<List<Anime>> SearchAsync(string query) => _jikan.SearchAsync(query);

    public async Task<Anime?> GetByIdAsync(int id)
    {
        var threshold = DateTime.UtcNow.Subtract(DetailCacheTtl);
        var cached = await _db.Animes
            .Include(a => a.Season)
            .FirstOrDefaultAsync(a => a.MALId == id && a.CachedAt != null && a.CachedAt > threshold);

        if (cached != null) return cached;

        var anime = await _jikan.GetByIdAsync(id);
        if (anime == null) return null;

        if (anime.MALId.HasValue && anime.MALId.Value > 0)
        {
            var existing = await _db.Animes.FirstOrDefaultAsync(a => a.MALId == anime.MALId);
            if (existing != null)
            {
                UpdateAnimeFields(existing, anime);
                existing.LocalImagePath = await _imageCache.CacheImageAsync(anime.ImageUrl, anime.MALId.Value);
                await _db.SaveChangesAsync();
                return existing;
            }

            var unknownSeason = await GetOrCreateSeason("unknown", 0);
            anime.SeasonId = unknownSeason.Id;
            anime.CachedAt = DateTime.UtcNow;
            anime.LocalImagePath = await _imageCache.CacheImageAsync(anime.ImageUrl, anime.MALId.Value);
            _db.Animes.Add(anime);
            await _db.SaveChangesAsync();
        }

        return anime;
    }

    public Task<List<Genre>> GetAllGenresAsync() => _jikan.GetAllGenresAsync();

    private async Task<List<Anime>> LoadCachedYear(int year)
    {
        var threshold = DateTime.UtcNow.Subtract(SeasonCacheTtl);
        var hasRecent = await _db.Animes
            .Include(a => a.Season)
            .AnyAsync(a => a.Season.Year == year && a.CachedAt != null && a.CachedAt > threshold);

        if (!hasRecent) return [];

        var all = await _db.Animes
            .Include(a => a.Season)
            .Where(a => a.Season.Year == year)
            .ToListAsync();

        // Deduplicate by MAL ID in memory — safety net for any stale DB duplicates
        return all
            .GroupBy(a => a.MALId.HasValue && a.MALId.Value > 0 ? a.MALId.Value : -a.Id)
            .Select(g => g.OrderByDescending(a => a.CachedAt ?? DateTime.MinValue).ThenByDescending(a => a.Id).First())
            .ToList();
    }

    private async Task<List<Anime>> FetchAndCacheYear(int year)
    {
        // Fetch each season sequentially to respect Jikan rate limit (~3 req/sec)
        var seasonAnimes = new List<(string Season, List<Anime> Animes)>();
        foreach (var s in AllSeasons)
        {
            var animes = await _jikan.GetSeasonAnimes(year, s);
            seasonAnimes.Add((s, animes));
            if (s != AllSeasons[^1])
                await Task.Delay(400);
        }

        // Ensure Season records exist
        var seasonIds = new Dictionary<string, int>();
        foreach (var (s, _) in seasonAnimes)
        {
            var record = await GetOrCreateSeason(s, year);
            seasonIds[s] = record.Id;
        }

        // Assign season IDs to each anime
        var allAnimes = seasonAnimes
            .SelectMany(x =>
            {
                foreach (var a in x.Animes) a.SeasonId = seasonIds[x.Season];
                return x.Animes;
            })
            .ToList();

        // Download images in parallel
        var imageTasks = allAnimes
            .Where(a => a.MALId.HasValue && a.MALId.Value > 0 && !string.IsNullOrEmpty(a.ImageUrl))
            .Select(async a => { a.LocalImagePath = await _imageCache.CacheImageAsync(a.ImageUrl, a.MALId!.Value); });
        await Task.WhenAll(imageTasks);

        await UpsertAnimesAsync(allAnimes);

        return await LoadCachedYear(year);
    }

    private async Task<Season> GetOrCreateSeason(string name, int year)
    {
        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.Name == name && s.Year == year);
        if (season != null) return season;

        season = new Season { Name = name, Year = year };
        _db.Seasons.Add(season);
        await _db.SaveChangesAsync();
        return season;
    }

    private async Task PurgeAllDuplicatesAsync()
    {
        var allWithMalIds = await _db.Animes
            .Where(a => a.MALId != null && a.MALId.Value > 0)
            .Select(a => new { a.Id, MalId = a.MALId!.Value })
            .ToListAsync();

        var idsToDelete = allWithMalIds
            .GroupBy(a => a.MalId)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(a => a.Id).Skip(1).Select(a => a.Id))
            .ToList();

        if (idsToDelete.Count > 0)
        {
            var toDelete = await _db.Animes.Where(a => idsToDelete.Contains(a.Id)).ToListAsync();
            _db.Animes.RemoveRange(toDelete);
            await _db.SaveChangesAsync();
        }
    }

    private async Task UpsertAnimesAsync(List<Anime> animes)
    {
        // Purge ALL duplicates in the DB before upserting, not just the current batch
        await PurgeAllDuplicatesAsync();

        var malIds = animes
            .Where(a => a.MALId.HasValue && a.MALId.Value > 0)
            .Select(a => a.MALId!.Value)
            .Distinct()
            .ToList();

        var existingRows = await _db.Animes
            .Where(a => a.MALId != null && malIds.Contains(a.MALId.Value))
            .ToListAsync();

        var existingByMalId = existingRows.ToDictionary(a => a.MALId!.Value);

        // Track MAL IDs seen in this batch to prevent duplicates across seasons in the same fetch
        var seenMalIds = new HashSet<int>(existingByMalId.Keys);

        foreach (var anime in animes)
        {
            if (anime.MALId.HasValue && anime.MALId.Value > 0)
            {
                if (existingByMalId.TryGetValue(anime.MALId.Value, out var existing))
                {
                    UpdateAnimeFields(existing, anime);
                    continue;
                }

                if (!seenMalIds.Add(anime.MALId.Value))
                    continue; // Already queued for insert earlier in this batch
            }

            // Title-based dedup fallback within same season (catches cross-source duplicates later)
            if (anime.SeasonId > 0)
            {
                var titleLower = anime.Title.ToLowerInvariant().Trim();
                var titleMatch = await _db.Animes.FirstOrDefaultAsync(a =>
                    a.SeasonId == anime.SeasonId &&
                    (a.Title.ToLower() == titleLower ||
                     (a.OriginalTitle != null && a.OriginalTitle.ToLower() == titleLower)));

                if (titleMatch != null)
                {
                    UpdateAnimeFields(titleMatch, anime);
                    continue;
                }
            }

            anime.CachedAt = DateTime.UtcNow;
            _db.Animes.Add(anime);
        }

        await _db.SaveChangesAsync();
    }

    private void UpdateAnimeFields(Anime existing, Anime source)
    {
        existing.Title = source.Title;
        existing.OriginalTitle = source.OriginalTitle;
        existing.Synopsis = source.Synopsis;
        existing.ImageUrl = source.ImageUrl;
        if (!string.IsNullOrEmpty(source.LocalImagePath))
            existing.LocalImagePath = source.LocalImagePath;
        existing.Type = source.Type;
        existing.Episodes = source.Episodes;
        existing.Rating = source.Rating;
        existing.Status = source.Status;
        existing.StartDate = source.StartDate;
        existing.EndDate = source.EndDate;
        existing.CachedAt = DateTime.UtcNow;
    }
}
