using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public interface IAnimeDataSource
{
    Task<List<Anime>> GetSeasonAnimes(int year, string season);
    Task<List<Anime>> SearchAsync(string query);
    Task<Anime?> GetByIdAsync(int id);
    Task<List<Genre>> GetAllGenresAsync();
}
