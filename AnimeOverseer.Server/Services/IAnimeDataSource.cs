using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public interface IAnimeDataSource
{
    Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false);
    Task<List<Anime>> SearchAsync(string query);
    Task<Anime?> GetByIdAsync(int id, bool forceRefresh = false);
    Task<List<Genre>> GetAllGenresAsync();
    Task<List<AnimeRelation>> GetAllRelationsAsync(int malId, bool forceRefresh = false);
}
