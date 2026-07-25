using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace AnimeOverseer.Server.Services;

/// <summary>The database is the catalogue identity boundary; provider IDs are optional metadata.</summary>
public class AnimeCacheService(AnimeDbContext db, AniListApiService aniList, ImageCacheService imageCache) : IAnimeDataSource
{
    private static readonly string[] AllSeasons = ["spring", "summer", "fall", "winter"];
    private IQueryable<Anime> Query() => db.Animes.Include(a => a.Season).Include(a => a.Images).Include(a => a.AnimeGenres).ThenInclude(x => x.Genre).Include(a => a.AnimeThemes).ThenInclude(x => x.Theme).Include(a => a.AnimeDemographics).ThenInclude(x => x.Demographic);

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = await Query().Where(a => a.Season.Year == year && a.Season.Name == season).ToListAsync();
            if (cached.Count > 0) return cached;
        }
        await FetchAndCacheSeasonsAsync(year, [season]);
        return await Query().Where(a => a.Season.Year == year && a.Season.Name == season).ToListAsync();
    }

    public async Task<List<Anime>> SearchAsync(string search)
    {
        var local = await Query().Where(a => a.Title.Contains(search) || (a.OriginalTitle != null && a.OriginalTitle.Contains(search))).ToListAsync();
        try
        {
            var remote = await aniList.SearchAsync(search);
            await UpsertAsync(remote, null);
            var ids = remote.Where(a => a.AniListId is > 0).Select(a => a.AniListId!.Value).ToList();
            var fetched = await Query().Where(a => a.AniListId != null && ids.Contains(a.AniListId.Value)).ToListAsync();
            return local.Concat(fetched).GroupBy(a => a.Id).Select(g => g.First()).OrderBy(a => a.Title).ToList();
        }
        catch { return local; }
    }

    // id is always this application's Anime.Id, never a provider ID.
    public async Task<Anime?> GetByIdAsync(int id, bool forceRefresh = false)
    {
        var cached = await Query().FirstOrDefaultAsync(a => a.Id == id);
        if (cached == null || !forceRefresh || cached.AniListId is not > 0) return cached;
        var remote = await aniList.GetByAniListIdAsync(cached.AniListId.Value);
        if (remote == null) return cached;
        await UpsertAsync([remote], cached.SeasonId);
        return await Query().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<bool> SetPreferredImageAsync(int animeId, int imageId)
    {
        var anime = await db.Animes.FirstOrDefaultAsync(item => item.Id == animeId);
        var imageExists = await db.AnimeImages.AnyAsync(image => image.Id == imageId && image.AnimeId == animeId);
        if (anime is null || !imageExists) return false;

        anime.PreferredImageId = imageId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<int>> FetchAndCacheSeasonsAsync(int year, string[] seasons, Func<int, int, Task>? onPageFetched = null, CancellationToken ct = default)
    {
        var syncedIds = new List<int>();
        foreach (var season in seasons)
        {
            var remote = await aniList.GetSeasonAnimesAsync(year, season, onPageFetched, ct);
            var seasonRow = await GetOrCreateSeasonAsync(season, year, ct);
            syncedIds.AddRange(await UpsertAsync(remote, seasonRow.Id, ct));
        }
        return syncedIds.Distinct().ToList();
    }

    /// <summary>Fetches a catalogue from one explicitly selected provider and persists it.</summary>
    public async Task<List<int>> FetchAndCacheSeasonsFromAsync(
        int year,
        string[] seasons,
        Func<int, string, CancellationToken, Task<List<Anime>>> fetch,
        Func<int, Task>? onSeasonFetched = null,
        CancellationToken ct = default)
    {
        var syncedIds = new List<int>();
        foreach (var season in seasons)
        {
            ct.ThrowIfCancellationRequested();
            var remote = await fetch(year, season, ct);
            if (onSeasonFetched is not null) await onSeasonFetched(remote.Count);
            var seasonRow = await GetOrCreateSeasonAsync(season, year, ct);
            syncedIds.AddRange(await UpsertAsync(remote, seasonRow.Id, ct));
        }
        return syncedIds.Distinct().ToList();
    }

    public async Task FetchAndCacheCurrentlyAiringAsync(Func<int, int, Task>? onPageFetched = null)
    {
        var airing = await aniList.GetCurrentAsync(false, onPageFetched);
        var upcoming = await aniList.GetCurrentAsync(true);
        foreach (var group in airing.Concat(upcoming).GroupBy(a => (Year: a.StartDate?.Year ?? DateTime.UtcNow.Year, Season: SeasonName(a.StartDate))))
        {
            var season = await GetOrCreateSeasonAsync(group.Key.Season, group.Key.Year, CancellationToken.None);
            await UpsertAsync(group.ToList(), season.Id);
        }
    }

    public async Task<List<AnimeRelation>> GetAllRelationsAsync(int animeId, bool forceRefresh = false)
    {
        var root = await db.Animes.FindAsync(animeId);
        if (root?.AniListId is not > 0) return [];
        var source = await aniList.GetByAniListIdAsync(root.AniListId.Value);
        if (source == null) return [];
        // Relation nodes are supplied in the selected GraphQL response; fetch their full records before caching.
        // The underlying JSON is intentionally not retained on Anime, so resolve the direct relations via a compact re-query.
        var related = await aniList.GetRelationsAsync(root.AniListId.Value);
        var result = new List<AnimeRelation>();
        foreach (var relation in related)
        {
            var item = await aniList.GetByAniListIdAsync(relation.AniListId);
            if (item == null) continue;
            await UpsertAsync([item], root.SeasonId);
            var saved = await db.Animes.SingleAsync(a => a.AniListId == item.AniListId);
            result.Add(new AnimeRelation { AnimeId = saved.Id, RelationType = relation.RelationType, Name = saved.Title });
        }
        return result;
    }

    public Task<List<Anime>> GetRecentAsync(int skip, int take) => Query().OrderByDescending(a => a.StartDate).Skip(skip).Take(take).ToListAsync();
    public Task<int> GetTotalCountAsync() => db.Animes.CountAsync();
    public Task<List<Anime>> GetFilteredAsync(FilterState state, int skip, int take) => FilterQueryBuilder.Apply(Query(), state).OrderByDescending(a => a.StartDate).Skip(skip).Take(take).ToListAsync();
    public Task<int> GetFilteredCountAsync(FilterState state) => FilterQueryBuilder.Apply(Query(), state).CountAsync();
    public Task<List<string>> GetGenreNamesAsync() => db.Genres.Select(x => x.Name).OrderBy(x => x).ToListAsync();
    public Task<List<string>> GetThemeNamesAsync() => db.Themes.Select(x => x.Name).OrderBy(x => x).ToListAsync();
    public Task<List<string>> GetDemographicNamesAsync() => db.Demographics.Select(x => x.Name).OrderBy(x => x).ToListAsync();
    public Task<List<Anime>> GetMostFavoritedAsync(int skip, int take) => Query().OrderByDescending(a => a.Requests.Count).Skip(skip).Take(take).ToListAsync();
    public Task<int> GetMostFavoritedCountAsync() => db.Animes.CountAsync();
    public Task<List<Genre>> GetAllGenresAsync() => db.Genres.OrderBy(x => x.Name).ToListAsync();
    public Task<List<Theme>> GetAllThemesAsync() => db.Themes.OrderBy(x => x.Name).ToListAsync();
    public Task<List<Demographic>> GetAllDemographicsAsync() => db.Demographics.OrderBy(x => x.Name).ToListAsync();

    private async Task<List<int>> UpsertAsync(List<Anime> incoming, int? fixedSeasonId, CancellationToken ct = default)
    {
        var aniListIds = incoming.Where(a => a.AniListId is > 0).Select(a => a.AniListId!.Value).Distinct().ToList();
        var malIds = incoming.Where(a => a.MALId is > 0).Select(a => a.MALId!.Value).Distinct().ToList();
        // IDs remain the primary identity. Load title candidates as well so a
        // provider record lacking a shared ID can still merge with its existing
        // English, romaji, native, or synonym title instead of creating a duplicate.
        var existingRows = await db.Animes.ToListAsync(ct);
        var existing = new Dictionary<string, Anime>();
        var existingByName = new Dictionary<string, HashSet<Anime>>(StringComparer.Ordinal);
        foreach (var row in existingRows)
        {
            if (row.AniListId is > 0) existing[$"a:{row.AniListId}"] = row;
            if (row.MALId is > 0) existing[$"m:{row.MALId}"] = row;
            AddNameCandidates(existingByName, row);
        }
        var sources = incoming.Where(a => a.AniListId is > 0 || a.MALId is > 0).GroupBy(Key).Select(g => g.First()).ToList();
        foreach (var source in sources)
        {
            if (!existing.TryGetValue(Key(source), out var target) &&
                !(source.MALId is > 0 && existing.TryGetValue($"m:{source.MALId}", out target)))
            {
                target = ResolveByName(existingByName, existingRows, source);
                if (target is null)
                {
                    target = new Anime { AniListId = source.AniListId, SeasonId = fixedSeasonId ?? await UnknownSeasonIdAsync(ct) };
                    db.Animes.Add(target);
                }
            }
            Copy(target, source);
            if (source.AniListId is > 0) existing[$"a:{source.AniListId}"] = target;
            if (source.MALId is > 0) existing[$"m:{source.MALId}"] = target;
            AddNameCandidates(existingByName, source, target);
            if (fixedSeasonId is > 0) target.SeasonId = fixedSeasonId.Value;
            target.CachedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        var syncedIds = sources.Select(source => existing[Key(source)].Id).Distinct().ToList();
        foreach (var source in sources)
        {
            var target = existing[Key(source)];
            if (!string.IsNullOrWhiteSpace(source.ImageUrl)) target.LocalImagePath = await imageCache.CacheImageAsync(source.ImageUrl, target.Id);
            await StoreImagesAsync(target, source, ct);
            await ReplaceTagsAsync(target, source, ct);
            // The same title may occur in more than one provider season. Keep
            // the next replacement independent of the joins tracked for this one.
            db.ChangeTracker.Clear();
        }
        return syncedIds;
    }

    private static string Key(Anime anime) => anime.AniListId is > 0 ? $"a:{anime.AniListId}" : $"m:{anime.MALId}";

    private static Anime? ResolveByName(Dictionary<string, HashSet<Anime>> index, IReadOnlyCollection<Anime> existingRows, Anime source)
    {
        var candidates = Names(source)
            .Where(index.ContainsKey)
            .SelectMany(name => index[name])
            .Distinct()
            .ToList();
        // Do not guess when a generic or reused title identifies more than one row.
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count > 1) return null;

        // Providers often vary only by articles and season notation, e.g.
        // "The Saga of Tanya the Evil II" vs "Saga of Tanya the Evil Season 2".
        // A match must be unambiguous and use at least two meaningful tokens.
        var ranked = existingRows
            .Select(row => new { Anime = row, Score = BestAliasScore(source, row) })
            .Where(x => x.Score >= 0.9)
            .OrderByDescending(x => x.Score)
            .ToList();
        return ranked.Count > 0 && (ranked.Count == 1 || ranked[0].Score > ranked[1].Score)
            ? ranked[0].Anime
            : null;
    }

    private static void AddNameCandidates(Dictionary<string, HashSet<Anime>> index, Anime source, Anime? target = null)
    {
        var resolved = target ?? source;
        foreach (var name in Names(source))
        {
            if (!index.TryGetValue(name, out var matches)) index[name] = matches = [];
            matches.Add(resolved);
        }
    }

    private static IEnumerable<string> Names(Anime anime)
        => new[] { anime.Title, anime.OriginalTitle }.Concat(anime.AlternativeTitles)
            .Select(NormalizeName).Where(name => name.Length >= 3).Distinct();

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static double BestAliasScore(Anime source, Anime existing)
    {
        var sourceNames = Names(source).Select(NameTokens).Where(tokens => tokens.Count >= 2).ToList();
        var existingNames = Names(existing).Select(NameTokens).Where(tokens => tokens.Count >= 2).ToList();
        return sourceNames.SelectMany(left => existingNames.Select(right => TokenScore(left, right))).DefaultIfEmpty(0).Max();
    }

    private static HashSet<string> NameTokens(string name)
    {
        var text = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(token => token.Length > 0 && token is not "the" and not "a" and not "an" and not "of" and not "season" and not "part")
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeToken(string token) => token switch
    {
        "i" => "1", "ii" => "2", "iii" => "3", "iv" => "4", "v" => "5",
        "vi" => "6", "vii" => "7", "viii" => "8", "ix" => "9", "x" => "10",
        _ => token
    };

    private static double TokenScore(HashSet<string> left, HashSet<string> right)
    {
        var union = left.Union(right).Count();
        return union == 0 ? 0 : (double)left.Intersect(right).Count() / union;
    }

    private static void Copy(Anime t, Anime s) { t.Title=s.Title; t.OriginalTitle=s.OriginalTitle; t.Synopsis=s.Synopsis; t.ImageUrl=s.ImageUrl; t.MALId=s.MALId; t.AniListId=s.AniListId; t.Type=s.Type; t.Episodes=s.Episodes; t.Duration=s.Duration; t.Rating=s.Rating; t.Status=s.Status; t.StartDate=s.StartDate; t.EndDate=s.EndDate; }
    private async Task StoreImagesAsync(Anime target, Anime source, CancellationToken ct)
    {
        var candidates = source.SourceImages.Count > 0
            ? source.SourceImages
            : string.IsNullOrWhiteSpace(source.ImageUrl) ? [] : [new SourceImage { Source = "Unknown", Type = "Poster", Url = source.ImageUrl }];
        foreach (var candidate in candidates.Where(image => !string.IsNullOrWhiteSpace(image.Url)).GroupBy(image => (image.Source, image.Type, image.Url)).Select(group => group.First()))
        {
            var image = await db.AnimeImages.FirstOrDefaultAsync(existing =>
                existing.AnimeId == target.Id && existing.Source == candidate.Source && existing.Type == candidate.Type && existing.ImageUrl == candidate.Url, ct);
            if (image is null)
            {
                image = new AnimeImage { AnimeId = target.Id, Source = candidate.Source, Type = candidate.Type, ImageUrl = candidate.Url };
                db.AnimeImages.Add(image);
                await db.SaveChangesAsync(ct);
            }
            image.LocalImagePath ??= await imageCache.CacheArtworkAsync(candidate.Url, target.Id, candidate.Source, candidate.Type);
        }
    }
    private async Task ReplaceTagsAsync(Anime target, Anime source, CancellationToken ct)
    {
        db.AnimeGenres.RemoveRange(await db.AnimeGenres.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        db.AnimeThemes.RemoveRange(await db.AnimeThemes.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        db.AnimeDemographics.RemoveRange(await db.AnimeDemographics.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        // A source can return the same anime in adjacent seasons. Persist the old
        // composite-key links before attaching replacements, otherwise EF tracks
        // a Deleted and Added link with the same (AnimeId, TagId) key at once.
        await db.SaveChangesAsync(ct);
        foreach (var name in source.AnimeGenres.Select(x => NormalizeTagName(x.Genre.Name)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) { var tag = await db.Genres.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct) ?? new Genre { Name=name }; if (tag.Id == 0) db.Genres.Add(tag); db.AnimeGenres.Add(new() { AnimeId=target.Id, Genre=tag }); }
        foreach (var name in source.AnimeThemes.Select(x => NormalizeTagName(x.Theme.Name)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) { var tag = await db.Themes.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct) ?? new Theme { Name=name }; if (tag.Id == 0) db.Themes.Add(tag); db.AnimeThemes.Add(new() { AnimeId=target.Id, Theme=tag }); }
        foreach (var name in source.AnimeDemographics.Select(x => NormalizeTagName(x.Demographic.Name)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) { var tag = await db.Demographics.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct) ?? new Demographic { Name=name }; if (tag.Id == 0) db.Demographics.Add(tag); db.AnimeDemographics.Add(new() { AnimeId=target.Id, Demographic=tag }); }
        await db.SaveChangesAsync(ct);
    }
    private static string NormalizeTagName(string? name) => name?.Trim() ?? string.Empty;
    private async Task<Season> GetOrCreateSeasonAsync(string name, int year, CancellationToken ct) { var season=await db.Seasons.FirstOrDefaultAsync(x=>x.Name==name&&x.Year==year,ct); if(season != null) return season; season=new Season{Name=name,Year=year}; db.Seasons.Add(season); await db.SaveChangesAsync(ct); return season; }
    private async Task<int> UnknownSeasonIdAsync(CancellationToken ct) => (await GetOrCreateSeasonAsync("unknown", 0, ct)).Id;
    private static string SeasonName(DateTime? date) => (date?.Month ?? DateTime.UtcNow.Month) switch { <=3=>"winter",<=6=>"spring",<=9=>"summer",_=>"fall" };
}
