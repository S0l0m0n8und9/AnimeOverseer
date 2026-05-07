using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class AnimeCacheService : IAnimeDataSource
{
    private static readonly string[] AllSeasons = ["spring", "summer", "fall", "winter"];

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
            var cached = await _db.Animes
                .Include(a => a.Season)
                .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
                .Include(a => a.AnimeThemes).ThenInclude(at => at.Theme)
                .Include(a => a.AnimeDemographics).ThenInclude(ad => ad.Demographic)
                .FirstOrDefaultAsync(a => a.MALId == id && a.CachedAt != null);

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
                .Include(a => a.AnimeThemes).ThenInclude(at => at.Theme)
                .Include(a => a.AnimeDemographics).ThenInclude(ad => ad.Demographic)
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

    public async Task<List<Anime>> GetRecentAsync(int skip, int take)
    {
        return await BaseQuery()
            .OrderByDescending(a => a.StartDate)
            .ThenByDescending(a => a.CachedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    private IQueryable<Anime> BaseQuery() => _db.Animes
        .Include(a => a.Season)
        .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
        .Include(a => a.AnimeThemes).ThenInclude(at => at.Theme)
        .Include(a => a.AnimeDemographics).ThenInclude(ad => ad.Demographic);

    public async Task<int> GetTotalCountAsync() => await _db.Animes.CountAsync();

    public async Task<List<Anime>> GetFilteredAsync(FilterState state, int skip, int take)
    {
        var query = BaseQuery();
        query = FilterQueryBuilder.Apply(query, state);
        return await query
            .OrderByDescending(a => a.StartDate)
            .ThenByDescending(a => a.CachedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetFilteredCountAsync(FilterState state)
    {
        return await FilterQueryBuilder.Apply(BaseQuery(), state).CountAsync();
    }

    public async Task<List<string>> GetGenreNamesAsync() =>
        (await _db.Genres.Select(g => g.Name).ToListAsync())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<List<string>> GetThemeNamesAsync() =>
        (await _db.Themes.Select(t => t.Name).ToListAsync())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<List<string>> GetDemographicNamesAsync() =>
        (await _db.Demographics.Select(d => d.Name).ToListAsync())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Task<List<Genre>> GetAllGenresAsync() => _jikan.GetAllGenresAsync();
    public Task<List<Theme>> GetAllThemesAsync() => _jikan.GetAllThemesAsync();
    public Task<List<Demographic>> GetAllDemographicsAsync() => _jikan.GetAllDemographicsAsync();

    public async Task<List<AnimeRelation>> GetAllRelationsAsync(int rootMalId, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = await _db.CachedAnimeRelations
                .Where(r => r.RootMalId == rootMalId)
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
        var hasAny = await _db.Animes
            .Include(a => a.Season)
            .AnyAsync(a => a.Season.Year == year && a.CachedAt != null);

        if (!hasAny) return [];

        var all = await _db.Animes
            .Include(a => a.Season)
            .Include(a => a.AnimeGenres).ThenInclude(ag => ag.Genre)
            .Include(a => a.AnimeThemes).ThenInclude(at => at.Theme)
            .Include(a => a.AnimeDemographics).ThenInclude(ad => ad.Demographic)
            .Where(a => a.Season.Year == year)
            .ToListAsync();

        // Deduplicate by MAL ID in memory — safety net for any stale DB duplicates
        return all
            .GroupBy(a => a.MALId.HasValue && a.MALId.Value > 0 ? a.MALId.Value : -a.Id)
            .Select(g => g.OrderByDescending(a => a.CachedAt ?? DateTime.MinValue).ThenByDescending(a => a.Id).First())
            .ToList();
    }

    // Public entry point for SyncService: fetch only the specified seasons with per-page progress callback.
    public Task FetchAndCacheSeasonsAsync(int year, string[] seasons, Func<int, int, Task>? onPageFetched = null)
        => FetchAndCacheYear(year, seasons, onPageFetched);

    // Public entry point for the daily airing refresh: fetch /seasons/now + /seasons/upcoming and upsert any changed records.
    public async Task FetchAndCacheCurrentlyAiringAsync(Func<int, int, Task>? onPageFetched = null)
    {
        var nowTask = _jikan.FetchSeasonNowPagesAsync(onPageFetched);
        var upcomingTask = _jikan.FetchSeasonUpcomingPagesAsync();
        await Task.WhenAll(nowTask, upcomingTask);

        var animes = nowTask.Result
            .Concat(upcomingTask.Result)
            .GroupBy(a => a.MALId)
            .Select(g => g.First())
            .ToList();

        // Assign season records derived from each anime's start date
        var groups = animes
            .GroupBy(a => (Year: a.StartDate?.Year ?? DateTime.UtcNow.Year, Season: GetSeasonFromDate(a.StartDate)))
            .ToList();

        foreach (var group in groups)
        {
            var record = await GetOrCreateSeason(group.Key.Season, group.Key.Year);
            foreach (var anime in group)
                anime.SeasonId = record.Id;
        }

        var (aniListByMalId, aniListByTitle) = await FetchAniListByYearAsync(DateTime.UtcNow.Year);

        foreach (var anime in animes.Where(NeedsEnrichment))
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

        var imageTasks = animes
            .Where(a => a.MALId.HasValue && a.MALId.Value > 0 && !string.IsNullOrEmpty(a.ImageUrl))
            .Select(async a => { a.LocalImagePath = await _imageCache.CacheImageAsync(a.ImageUrl, a.MALId!.Value); });
        await Task.WhenAll(imageTasks);

        await UpsertAnimesAsync(animes);
    }

    private static string GetSeasonFromDate(DateTime? date) =>
        (date?.Month ?? DateTime.UtcNow.Month) switch
        {
            <= 3 => "winter",
            <= 6 => "spring",
            <= 9 => "summer",
            _    => "fall"
        };

    private async Task<List<Anime>> FetchAndCacheYear(int year,
        string[]? selectedSeasons = null, Func<int, int, Task>? onPageFetched = null)
    {
        var seasonsToFetch = selectedSeasons ?? AllSeasons;

        // Fetch each season sequentially to respect Jikan rate limit (~3 req/sec)
        var seasonAnimes = new List<(string Season, List<Anime> Animes)>();
        foreach (var s in seasonsToFetch)
        {
            var animes = await _jikan.FetchSeasonPagesAsync(year, s, onPageFetched);
            seasonAnimes.Add((s, animes));
            if (s != seasonsToFetch[^1])
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
        var sourcesWithData = sourceAnimes
            .Where(a => a.MALId.HasValue && a.MALId.Value > 0 &&
                        (a.AnimeGenres.Count > 0 || a.AnimeThemes.Count > 0 || a.AnimeDemographics.Count > 0))
            .ToList();
        if (sourcesWithData.Count == 0) return;

        var malIds = sourcesWithData.Select(a => a.MALId!.Value).ToList();
        var dbAnimes = await _db.Animes
            .Where(a => a.MALId != null && malIds.Contains(a.MALId.Value))
            .ToListAsync();
        var dbByMalId = dbAnimes.ToDictionary(a => a.MALId!.Value);
        var dbIds = dbAnimes.Select(a => a.Id).ToList();

        await SyncTagsAsync(
            sourcesWithData,
            dbByMalId,
            dbIds,
            a => a.AnimeGenres.Select(ag => ag.Genre.Name.Trim()),
            names => _db.Genres.Where(g => names.Contains(g.Name)).ToListAsync(),
            name => new Genre { Name = name },
            (g, existing) => existing.TryAdd(g.Name, g),
            (animeId, tag) => _db.AnimeGenres.Add(new AnimeGenre { AnimeId = animeId, GenreId = tag.Id }),
            animeIds => _db.AnimeGenres.Where(ag => animeIds.Contains(ag.AnimeId)).ToListAsync(),
            links => _db.AnimeGenres.RemoveRange(links));

        await SyncTagsAsync(
            sourcesWithData,
            dbByMalId,
            dbIds,
            a => a.AnimeThemes.Select(at => at.Theme.Name.Trim()),
            names => _db.Themes.Where(t => names.Contains(t.Name)).ToListAsync(),
            name => new Theme { Name = name },
            (t, existing) => existing.TryAdd(t.Name, t),
            (animeId, tag) => _db.AnimeThemes.Add(new AnimeTheme { AnimeId = animeId, ThemeId = tag.Id }),
            animeIds => _db.AnimeThemes.Where(at => animeIds.Contains(at.AnimeId)).ToListAsync(),
            links => _db.AnimeThemes.RemoveRange(links));

        await SyncTagsAsync(
            sourcesWithData,
            dbByMalId,
            dbIds,
            a => a.AnimeDemographics.Select(ad => ad.Demographic.Name.Trim()),
            names => _db.Demographics.Where(d => names.Contains(d.Name)).ToListAsync(),
            name => new Demographic { Name = name },
            (d, existing) => existing.TryAdd(d.Name, d),
            (animeId, tag) => _db.AnimeDemographics.Add(new AnimeDemographic { AnimeId = animeId, DemographicId = tag.Id }),
            animeIds => _db.AnimeDemographics.Where(ad => animeIds.Contains(ad.AnimeId)).ToListAsync(),
            links => _db.AnimeDemographics.RemoveRange(links));
    }

    private async Task SyncTagsAsync<TLink, TTag>(
        List<Anime> sources,
        Dictionary<int, Anime> dbByMalId,
        List<int> dbIds,
        Func<Anime, IEnumerable<string>> getNames,
        Func<List<string>, Task<List<TTag>>> loadExisting,
        Func<string, TTag> createTag,
        Action<TTag, Dictionary<string, TTag>> addToDict,
        Action<int, TTag> addLink,
        Func<List<int>, Task<List<TLink>>> loadOldLinks,
        Action<List<TLink>> removeLinks)
        where TTag : class
        where TLink : class
    {
        // Collect names from sources that have entries in this category
        var sourcesWithTags = sources.Where(a => getNames(a).Any()).ToList();
        if (sourcesWithTags.Count == 0) return;

        var allNames = sourcesWithTags
            .SelectMany(getNames)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingRows = await loadExisting(allNames);
        var existing = new Dictionary<string, TTag>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existingRows)
            addToDict(tag, existing);

        // Cast to access Name property via dynamic to keep the helper generic
        foreach (var name in allNames.Where(n => !existing.ContainsKey(n)))
        {
            var tag = createTag(name);
            _db.Add(tag);
            existing[name] = tag;
        }
        await _db.SaveChangesAsync();

        var affectedIds = sourcesWithTags
            .Where(a => dbByMalId.ContainsKey(a.MALId!.Value))
            .Select(a => dbByMalId[a.MALId!.Value].Id)
            .ToList();

        var oldLinks = await loadOldLinks(affectedIds);
        removeLinks(oldLinks);

        var addedLinks = new HashSet<(int, int)>();
        foreach (var source in sourcesWithTags)
        {
            if (!dbByMalId.TryGetValue(source.MALId!.Value, out var dbAnime)) continue;
            foreach (var name in getNames(source).ToList())
            {
                if (string.IsNullOrEmpty(name) || !existing.TryGetValue(name, out var tag)) continue;
                var tagId = (int)((dynamic)tag).Id;
                if (!addedLinks.Add((dbAnime.Id, tagId))) continue;
                addLink(dbAnime.Id, tag);
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
