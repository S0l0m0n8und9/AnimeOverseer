using System.Net.Http.Json;

namespace AnimeOverseer.Server.Services;

public class AnimeApiClient(HttpClient httpClient)
{
    private const string BaseApiPath = "api/anime";

    public async Task<List<Models.Anime>> GetSeasonAnimesAsync(int year, string season)
    {
        var result = await httpClient.GetFromJsonAsync<List<Models.Anime>>($"{BaseApiPath}/season?year={year}&season={season}");
        return result ?? [];
    }

    public async Task<List<Models.Anime>> SearchAnimeAsync(string query)
    {
        var result = await httpClient.GetFromJsonAsync<List<Models.Anime>>($"{BaseApiPath}/search?q={Uri.EscapeDataString(query)}");
        return result ?? [];
    }

    public async Task<Models.Anime?> GetAnimeByIdAsync(int id)
    {
        var response = await httpClient.GetAsync($"{BaseApiPath}/{id}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Models.Anime>();
    }

    public async Task<List<Models.Genre>> GetAllGenresAsync()
    {
        var result = await httpClient.GetFromJsonAsync<List<Models.Genre>>($"{BaseApiPath}/genres");
        return result ?? [];
    }
}
