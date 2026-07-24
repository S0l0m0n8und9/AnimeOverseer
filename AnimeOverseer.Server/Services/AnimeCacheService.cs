using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

/// <summary>The database is the catalogue identity boundary; provider IDs are optional metadata.</summary>
public class AnimeCacheService(AnimeDbContext db, AniListApiService aniList, MyAnimeListApiService myAnimeList, ImageCacheService imageCache) : IAnimeDataSource
{
    private static readonly string[] AllSeasons = ["spring", "summer", "fall", "winter"];
    private IQueryable<Anime> Query() => db.Animes.Include(a => a.Season).Include(a => a.AnimeGenres).ThenInclude(x => x.Genre).Include(a => a.AnimeThemes).ThenInclude(x => x.Theme).Include(a => a.AnimeDemographics).ThenInclude(x => x.Demographic);

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

    public async Task FetchAndCacheSeasonsAsync(int year, string[] seasons, Func<int, int, Task>? onPageFetched = null, CancellationToken ct = default)
    {
        foreach (var season in seasons)
        {
            List<Anime> remote;
            try { remote = await aniList.GetSeasonAnimesAsync(year, season, onPageFetched, ct); }
            catch when (myAnimeList.IsConfigured) { remote = await myAnimeList.GetSeasonAsync(year, season, ct); }
            var seasonRow = await GetOrCreateSeasonAsync(season, year, ct);
            await UpsertAsync(remote, seasonRow.Id, ct);
        }
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

    private async Task UpsertAsync(List<Anime> incoming, int? fixedSeasonId, CancellationToken ct = default)
    {
        var aniListIds = incoming.Where(a => a.AniListId is > 0).Select(a => a.AniListId!.Value).Distinct().ToList();
        var malIds = incoming.Where(a => a.MALId is > 0).Select(a => a.MALId!.Value).Distinct().ToList();
        var existingRows = await db.Animes.Where(a => (a.AniListId != null && aniListIds.Contains(a.AniListId.Value)) || (a.MALId != null && malIds.Contains(a.MALId.Value))).ToListAsync(ct);
        var existing = new Dictionary<string, Anime>();
        foreach (var row in existingRows)
        {
            if (row.AniListId is > 0) existing[$"a:{row.AniListId}"] = row;
            if (row.MALId is > 0) existing[$"m:{row.MALId}"] = row;
        }
        var sources = incoming.Where(a => a.AniListId is > 0 || a.MALId is > 0).GroupBy(Key).Select(g => g.First()).ToList();
        foreach (var source in sources)
        {
            if (!existing.TryGetValue(Key(source), out var target) &&
                !(source.MALId is > 0 && existing.TryGetValue($"m:{source.MALId}", out target)))
            {
                target = new Anime { AniListId = source.AniListId, SeasonId = fixedSeasonId ?? await UnknownSeasonIdAsync(ct) };
                db.Animes.Add(target);
            }
            Copy(target, source);
            if (source.AniListId is > 0) existing[$"a:{source.AniListId}"] = target;
            if (source.MALId is > 0) existing[$"m:{source.MALId}"] = target;
            if (fixedSeasonId is > 0) target.SeasonId = fixedSeasonId.Value;
            target.CachedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        foreach (var source in sources)
        {
            var target = existing[Key(source)];
            if (!string.IsNullOrWhiteSpace(source.ImageUrl)) target.LocalImagePath = await imageCache.CacheImageAsync(source.ImageUrl, target.Id);
            await ReplaceTagsAsync(target, source, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Key(Anime anime) => anime.AniListId is > 0 ? $"a:{anime.AniListId}" : $"m:{anime.MALId}";

    private static void Copy(Anime t, Anime s) { t.Title=s.Title; t.OriginalTitle=s.OriginalTitle; t.Synopsis=s.Synopsis; t.ImageUrl=s.ImageUrl; t.MALId=s.MALId; t.AniListId=s.AniListId; t.Type=s.Type; t.Episodes=s.Episodes; t.Duration=s.Duration; t.Rating=s.Rating; t.Status=s.Status; t.StartDate=s.StartDate; t.EndDate=s.EndDate; }
    private async Task ReplaceTagsAsync(Anime target, Anime source, CancellationToken ct)
    {
        db.AnimeGenres.RemoveRange(await db.AnimeGenres.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        db.AnimeThemes.RemoveRange(await db.AnimeThemes.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        db.AnimeDemographics.RemoveRange(await db.AnimeDemographics.Where(x => x.AnimeId == target.Id).ToListAsync(ct));
        foreach (var name in source.AnimeGenres.Select(x => x.Genre.Name).Distinct()) { var tag = await db.Genres.FirstOrDefaultAsync(x => x.Name == name, ct) ?? new Genre { Name=name }; if (tag.Id == 0) db.Genres.Add(tag); db.AnimeGenres.Add(new() { AnimeId=target.Id, Genre=tag }); }
        foreach (var name in source.AnimeThemes.Select(x => x.Theme.Name).Distinct()) { var tag = await db.Themes.FirstOrDefaultAsync(x => x.Name == name, ct) ?? new Theme { Name=name }; if (tag.Id == 0) db.Themes.Add(tag); db.AnimeThemes.Add(new() { AnimeId=target.Id, Theme=tag }); }
        foreach (var name in source.AnimeDemographics.Select(x => x.Demographic.Name).Distinct()) { var tag = await db.Demographics.FirstOrDefaultAsync(x => x.Name == name, ct) ?? new Demographic { Name=name }; if (tag.Id == 0) db.Demographics.Add(tag); db.AnimeDemographics.Add(new() { AnimeId=target.Id, Demographic=tag }); }
    }
    private async Task<Season> GetOrCreateSeasonAsync(string name, int year, CancellationToken ct) { var season=await db.Seasons.FirstOrDefaultAsync(x=>x.Name==name&&x.Year==year,ct); if(season != null) return season; season=new Season{Name=name,Year=year}; db.Seasons.Add(season); await db.SaveChangesAsync(ct); return season; }
    private async Task<int> UnknownSeasonIdAsync(CancellationToken ct) => (await GetOrCreateSeasonAsync("unknown", 0, ct)).Id;
    private static string SeasonName(DateTime? date) => (date?.Month ?? DateTime.UtcNow.Month) switch { <=3=>"winter",<=6=>"spring",<=9=>"summer",_=>"fall" };
}
