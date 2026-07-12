using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services
{
    public interface IAnimeDataSource
    {
        Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false);
        Task<List<Anime>> GetRecentAsync(int skip, int take);
        Task<int> GetTotalCountAsync();
        Task<List<Anime>> GetFilteredAsync(FilterState state, int skip, int take);
        Task<int> GetFilteredCountAsync(FilterState state);
        Task<List<string>> GetGenreNamesAsync();
        Task<List<string>> GetThemeNamesAsync();
        Task<List<string>> GetDemographicNamesAsync();
        Task<List<Anime>> SearchAsync(string query);
        Task<Anime?> GetByIdAsync(int id, bool forceRefresh = false);
        Task<List<Genre>> GetAllGenresAsync();
        Task<List<Theme>> GetAllThemesAsync();
        Task<List<Demographic>> GetAllDemographicsAsync();
        Task<List<AnimeRelation>> GetAllRelationsAsync(int malId, bool forceRefresh = false);
        Task<List<Anime>> GetMostFavoritedAsync(int skip, int take);
        Task<int> GetMostFavoritedCountAsync();
    }
}
