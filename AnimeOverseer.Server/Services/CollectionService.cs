using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AnimeOverseer.Server.Services;

public class CollectionService(AnimeDbContext db)
{
    private IQueryable<Anime> Animes() => db.Animes.AsNoTracking()
        .Include(a => a.Season).Include(a => a.Images)
        .Include(a => a.AnimeGenres).ThenInclude(link => link.Genre)
        .Include(a => a.AnimeThemes).ThenInclude(link => link.Theme)
        .Include(a => a.AnimeDemographics).ThenInclude(link => link.Demographic);

    public Task<List<AnimeCollection>> GetAllAsync() => db.AnimeCollections.AsNoTracking()
        .Include(collection => collection.Items)
        .OrderBy(collection => collection.Name).ToListAsync();

    public Task<AnimeCollection?> GetAsync(int id) => db.AnimeCollections.AsNoTracking()
        .Include(collection => collection.Items).ThenInclude(item => item.Anime).ThenInclude(anime => anime.Images)
        .Include(collection => collection.Items).ThenInclude(item => item.Anime).ThenInclude(anime => anime.Season)
        .FirstOrDefaultAsync(collection => collection.Id == id);

    public async Task<AnimeCollection> CreateAsync(string name, string? description, CollectionType type, FilterState? filters)
    {
        var collection = new AnimeCollection { Name = name.Trim(), Description = description?.Trim(), Type = type, FilterJson = type == CollectionType.Automatic ? JsonSerializer.Serialize(filters ?? new FilterState()) : null };
        db.AnimeCollections.Add(collection);
        await db.SaveChangesAsync();
        return collection;
    }

    public async Task<bool> UpdateAsync(int id, string name, string? description, CollectionType type, FilterState? filters)
    {
        var collection = await db.AnimeCollections.FindAsync(id);
        if (collection is null) return false;
        collection.Name = name.Trim(); collection.Description = description?.Trim(); collection.Type = type;
        collection.FilterJson = type == CollectionType.Automatic ? JsonSerializer.Serialize(filters ?? new FilterState()) : null;
        collection.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        var collection = await db.AnimeCollections.FindAsync(id);
        if (collection is null) return;
        db.AnimeCollections.Remove(collection);
        await db.SaveChangesAsync();
    }

    public async Task<List<Anime>> GetAnimeAsync(AnimeCollection collection)
    {
        if (collection.Type == CollectionType.Manual)
            return await Animes().Where(anime => db.AnimeCollectionItems.Any(item => item.CollectionId == collection.Id && item.AnimeId == anime.Id)).OrderBy(anime => anime.Title).ToListAsync();
        var savedFilters = Deserialize(collection.FilterJson);
        if (CollectionFilterExpressionResolver.RequiresAnimeContext(savedFilters))
        {
            var animes = await Animes().ToListAsync();
            return animes.Where(anime => FilterEngine.Apply([anime], CollectionFilterExpressionResolver.Resolve(savedFilters, anime: anime)).Count == 1)
                .OrderByDescending(anime => anime.StartDate).ToList();
        }
        return await FilterQueryBuilder.Apply(Animes(), CollectionFilterExpressionResolver.Resolve(savedFilters)).OrderByDescending(anime => anime.StartDate).ToListAsync();
    }

    public async Task<bool> AddAnimeAsync(int collectionId, int animeId)
    {
        if (!await db.AnimeCollections.AnyAsync(collection => collection.Id == collectionId && collection.Type == CollectionType.Manual) || !await db.Animes.AnyAsync(anime => anime.Id == animeId)) return false;
        if (await db.AnimeCollectionItems.AnyAsync(item => item.CollectionId == collectionId && item.AnimeId == animeId)) return true;
        db.AnimeCollectionItems.Add(new AnimeCollectionItem { CollectionId = collectionId, AnimeId = animeId });
        await db.SaveChangesAsync(); return true;
    }

    public async Task RemoveAnimeAsync(int collectionId, int animeId)
    {
        var item = await db.AnimeCollectionItems.FindAsync(collectionId, animeId);
        if (item is null) return;
        db.AnimeCollectionItems.Remove(item); await db.SaveChangesAsync();
    }

    public async Task<List<Anime>> SearchAnimeAsync(string query) => string.IsNullOrWhiteSpace(query) ? [] : await Animes()
        .Where(anime => anime.Title.Contains(query) || (anime.OriginalTitle != null && anime.OriginalTitle.Contains(query)))
        .OrderBy(anime => anime.Title).Take(12).ToListAsync();

    public static FilterState Deserialize(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? new FilterState() : JsonSerializer.Deserialize<FilterState>(json) ?? new FilterState(); }
        catch { return new FilterState(); }
    }
}
