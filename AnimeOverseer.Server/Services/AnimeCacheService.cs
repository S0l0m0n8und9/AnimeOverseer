using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class AnimeCacheService : IAnimeDataSource
{
    private static readonly string[] AllSeasons = ["spring", "summer", "fall", "winter"];
    private static readonly TimeSpan SeasonCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RelationCacheTtl = TimeSpan.FromDays(7);

    private readonly AnimeDbContext _db;
    private readonly JikanApiService _jikan;
    private readonly AniListApiService _aniList;
    private readonly KitsuApiService _kitsu;
    private readonly ImageCacheService _imageCache;

    public AnimeCacheService(AnimeDbContext db, JikanApiService jikan, AniListApiService aniList, KitsuApiService kitsu, ImageCacheService imageCache)
    {
        _db = db;
        _jikan = jikan;
        _aniList = aniList;
        _kitsu = kitsu;
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

    public async Task<Anime?> GetByIdAsync(int id, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var threshold = DateTime.UtcNow.Subtract(DetailCacheTtl);
            var cached = await _db.Animes
                .Include(a => a.Season)
                .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
                .FirstOrDefaultAsync(a => a.MALId == id && a.CachedAt != null && a.CachedAt > threshold);

            if (cached != null) return cached;
        }

        var anime = await _jikan.GetByIdAsync(id);
        if (anime == null) return null;

        await EnrichSingleAsync(anime);

        if (anime.MALId.HasValue && anime.MALId.Value > 0)
        {
            var existing = await _db.Animes
                .Include(a => a.Season)
                .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
                .FirstOrDefaultAsync(a => a.MALId == anime.MALId);
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

    public async Task<List<AnimeRelation>> GetAllRelationsAsync(int rootMalId, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var threshold = DateTime.UtcNow.Subtract(RelationCacheTtl);
            var cached = await _db.CachedAnimeRelations
                .Where(r => r.RootMalId == rootMalId && r.CachedAt > threshold)
                .ToListAsync();

            if (cached.Count > 0)
                return cached.Select(r => new AnimeRelation { RelationType = r.RelationType, MALId = r.RelatedMalId, Name = r.Name }).ToList();
        }

        var allRelations = await FetchRelationsBfsAsync(rootMalId);

        var stale = await _db.CachedAnimeRelations.Where(r => r.RootMalId == rootMalId).ToListAsync();
        _db.CachedAnimeRelations.RemoveRange(stale);

        var now = DateTime.UtcNow;
        _db.CachedAnimeRelations.AddRange(allRelations.Select(r => new CachedAnimeRelation
        {
            RootMalId = rootMalId,
            RelatedMalId = r.MALId,
            RelationType = r.RelationType,
            Name = r.Name,
            CachedAt = now
        }));
        await _db.SaveChangesAsync();

        return allRelations;
    }

    private async Task<List<AnimeRelation>> FetchRelationsBfsAsync(int rootMalId)
    {
        const int maxNodes = 25;
        var visited = new HashSet<int> { rootMalId };
        var queue = new Queue<int>();
        var allRelations = new List<AnimeRelation>();

        var initial = await _jikan.GetRelationsAsync(rootMalId);
        foreach (var rel in initial.Where(r => visited.Add(r.MALId)))
        {
            allRelations.Add(rel);
            queue.Enqueue(rel.MALId);
        }

        while (queue.Count > 0 && allRelations.Count < maxNodes)
        {
            await Task.Delay(400); // respect Jikan rate limit
            var malId = queue.Dequeue();
            var relations = await _jikan.GetRelationsAsync(malId);

            foreach (var rel in relations.Where(r => r.MALId != rootMalId && visited.Add(r.MALId)))
            {
                allRelations.Add(rel);
                if (allRelations.Count < maxNodes)
                    queue.Enqueue(rel.MALId);
            }
        }

        return allRelations;
    }

    // Returns true when an anime is missing commonly-useful fields worth enriching from secondary sources.
    private static bool NeedsEnrichment(Anime a) => true;
        //string.IsNullOrWhiteSpace(a.Synopsis) ||
        //a.Episodes == null ||
        //a.Rating == null;

    // Copies fields from source into target only where target has no data.
    private static void EnrichAnimeFields(Anime target, Anime source)
    {
        if (string.IsNullOrWhiteSpace(target.Synopsis) && !string.IsNullOrWhiteSpace(source.Synopsis))
            target.Synopsis = source.Synopsis;
        if (target.Episodes == null && source.Episodes != null)
            target.Episodes = source.Episodes;
        if (target.Rating == null && source.Rating != null)
            target.Rating = source.Rating;
        if (string.IsNullOrWhiteSpace(target.OriginalTitle) && !string.IsNullOrWhiteSpace(source.OriginalTitle))
            target.OriginalTitle = source.OriginalTitle;
        if (string.IsNullOrWhiteSpace(target.ImageUrl) && !string.IsNullOrWhiteSpace(source.ImageUrl))
            target.ImageUrl = source.ImageUrl;
        if ((target.AniListId == null || target.AniListId == 0) && source.AniListId is > 0)
            target.AniListId = source.AniListId;
        if ((target.KitsuId == null || target.KitsuId == 0) && source.KitsuId is > 0)
            target.KitsuId = source.KitsuId;
    }

    // Enriches a single anime in-place using AniList (by MAL ID) then Kitsu (by title) as fallbacks.
    private async Task EnrichSingleAsync(Anime anime)
    {
        if (!NeedsEnrichment(anime)) return;

        if (anime.MALId is > 0)
        {
            try
            {
                var aniListData = await _aniList.GetByMalIdAsync(anime.MALId.Value);
                if (aniListData != null) EnrichAnimeFields(anime, aniListData);
            }
            catch { }
        }

        if (NeedsEnrichment(anime) && !string.IsNullOrWhiteSpace(anime.Title))
        {
            try
            {
                var kitsuData = await _kitsu.GetByTitleAsync(anime.Title);
                if (kitsuData != null) EnrichAnimeFields(anime, kitsuData);
            }
            catch { }
        }
    }

    // Fetches AniList data for all seasons in a year and returns lookups by MAL ID and by title.
    private async Task<(Dictionary<int, Anime> ByMalId, Dictionary<string, Anime> ByTitle)> FetchAniListByYearAsync(int year)
    {
        var tasks = AllSeasons.Select(s => _aniList.GetSeasonAnimes(year, s));
        var results = await Task.WhenAll(tasks);
        var all = results.SelectMany(r => r).ToList();

        var byMalId = all
            .Where(a => a.MALId is > 0)
            .GroupBy(a => a.MALId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // Title lookup covers anime where AniList didn't provide idMal
        var byTitle = new Dictionary<string, Anime>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
        {
            if (!string.IsNullOrWhiteSpace(a.Title))
                byTitle.TryAdd(a.Title.Trim(), a);
            if (!string.IsNullOrWhiteSpace(a.OriginalTitle))
                byTitle.TryAdd(a.OriginalTitle.Trim(), a);
        }

        return (byMalId, byTitle);
    }

    private async Task<List<Anime>> LoadCachedYear(int year)
    {
        var threshold = DateTime.UtcNow.Subtract(SeasonCacheTtl);
        var hasRecent = await _db.Animes
            .Include(a => a.Season)
            .AnyAsync(a => a.Season.Year == year && a.CachedAt != null && a.CachedAt > threshold);

        if (!hasRecent) return [];

        var all = await _db.Animes
            .Include(a => a.Season)
            .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
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

        // Fetch AniList data for the same year in parallel (no strict rate limit)
        var (aniListByMalId, aniListByTitle) = await FetchAniListByYearAsync(year);

        // Ensure Season records exist
        var seasonIds = new Dictionary<string, int>();
        foreach (var (s, _) in seasonAnimes)
        {
            var record = await GetOrCreateSeason(s, year);
            seasonIds[s] = record.Id;
        }

        // Assign season IDs and enrich from AniList where Jikan data is incomplete
        var allAnimes = seasonAnimes
            .SelectMany(x =>
            {
                foreach (var a in x.Animes) a.SeasonId = seasonIds[x.Season];
                return x.Animes;
            })
            .ToList();

        foreach (var anime in allAnimes.Where(NeedsEnrichment))
        {
            Anime? aniListMatch = null;

            if (anime.MALId is > 0)
                aniListByMalId.TryGetValue(anime.MALId.Value, out aniListMatch);

            if (aniListMatch == null && !string.IsNullOrWhiteSpace(anime.Title))
                aniListByTitle.TryGetValue(anime.Title.Trim(), out aniListMatch);

            if (aniListMatch == null && !string.IsNullOrWhiteSpace(anime.OriginalTitle))
                aniListByTitle.TryGetValue(anime.OriginalTitle.Trim(), out aniListMatch);

            if (aniListMatch != null)
                EnrichAnimeFields(anime, aniListMatch);
        }

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
        await SyncGenresBatchAsync(animes);
    }

    private async Task SyncGenresBatchAsync(List<Anime> sourceAnimes)
    {
        var sourcesWithGenres = sourceAnimes
            .Where(a => a.MALId.HasValue && a.MALId.Value > 0 && a.AnimeGenres.Count > 0)
            .ToList();
        if (sourcesWithGenres.Count == 0) return;

        // Collect all unique genre names
        var allNames = sourcesWithGenres
            .SelectMany(a => a.AnimeGenres.Select(ag => ag.Genre.Name.Trim()))
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Load or create Genre rows — use TryAdd to survive any case-variant duplicates in the DB
        var existingGenreRows = await _db.Genres
            .Where(g => allNames.Contains(g.Name))
            .ToListAsync();
        var existing = new Dictionary<string, Genre>(StringComparer.OrdinalIgnoreCase);
        foreach (var genre in existingGenreRows)
            existing.TryAdd(genre.Name, genre);

        foreach (var name in allNames.Where(n => !existing.ContainsKey(n)))
        {
            var g = new Genre { Name = name };
            _db.Genres.Add(g);
            existing[name] = g;
        }
        await _db.SaveChangesAsync();

        // Load DB animes for these MAL IDs
        var malIds = sourcesWithGenres.Select(a => a.MALId!.Value).ToList();
        var dbAnimes = await _db.Animes
            .Where(a => a.MALId != null && malIds.Contains(a.MALId.Value))
            .ToListAsync();
        var dbByMalId = dbAnimes.ToDictionary(a => a.MALId!.Value);

        // Remove old genre links and re-add
        var dbIds = dbAnimes.Select(a => a.Id).ToList();
        var oldLinks = await _db.AnimeGenres.Where(ag => dbIds.Contains(ag.AnimeId)).ToListAsync();
        _db.AnimeGenres.RemoveRange(oldLinks);

        var addedLinks = new HashSet<(int AnimeId, int GenreId)>();
        foreach (var source in sourcesWithGenres)
        {
            if (!dbByMalId.TryGetValue(source.MALId!.Value, out var dbAnime)) continue;
            foreach (var ag in source.AnimeGenres)
            {
                var name = ag.Genre.Name.Trim();
                if (string.IsNullOrEmpty(name) || !existing.TryGetValue(name, out var genre)) continue;
                if (!addedLinks.Add((dbAnime.Id, genre.Id))) continue;
                _db.AnimeGenres.Add(new AnimeGenre { AnimeId = dbAnime.Id, GenreId = genre.Id });
            }
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
        if (source.AniListId is > 0) existing.AniListId = source.AniListId;
        if (source.KitsuId is > 0) existing.KitsuId = source.KitsuId;
        existing.CachedAt = DateTime.UtcNow;
    }
}
