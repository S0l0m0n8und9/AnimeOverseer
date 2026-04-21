using System.Net.Http.Json;
using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public class KitsuApiService : IAnimeDataSource
{
    private readonly HttpClient _httpClient;
    private const string ApiUrl = "https://kitsu.io/api/edge";

    public KitsuApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        var animes = new List<Anime>();
        // Kitsu doesn't have direct season filtering, so we'll return popular anime
        try
        {
            var response = await _httpClient.GetAsync($"{ApiUrl}/anime?filter[season]={season.ToLower()}&filter[year]={year}&limit=25");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");

                foreach (var anime in data.EnumerateArray())
                {
                    animes.Add(MapFromKitsu(anime, year, season));
                }
            }
        }
        catch
        {
            // Handle errors gracefully
        }
        return animes;
    }

    public async Task<List<Anime>> SearchAsync(string query)
    {
        var animes = new List<Anime>();
        try
        {
            var response = await _httpClient.GetAsync($"{ApiUrl}/anime?filter[text]={Uri.EscapeDataString(query)}&limit=25");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");

                foreach (var anime in data.EnumerateArray())
                {
                    animes.Add(MapFromKitsu(anime, 0, ""));
                }
            }
        }
        catch
        {
            // Handle errors gracefully
        }
        return animes;
    }

    public async Task<Anime?> GetByIdAsync(int id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiUrl}/anime/{id}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                return MapFromKitsu(data, 0, "");
            }
        }
        catch
        {
            // Handle errors gracefully
        }
        return null;
    }

    public Task<List<Genre>> GetAllGenresAsync()
    {
        var genres = new List<Genre>
        {
            new() { Id = 1, Name = "Action" },
            new() { Id = 2, Name = "Adventure" },
            new() { Id = 3, Name = "Comedy" },
            new() { Id = 4, Name = "Drama" },
            new() { Id = 5, Name = "Fantasy" },
            new() { Id = 6, Name = "Horror" },
            new() { Id = 7, Name = "Mystery" },
            new() { Id = 8, Name = "Romance" },
            new() { Id = 9, Name = "Sci-Fi" },
            new() { Id = 10, Name = "Slice of Life" },
            new() { Id = 11, Name = "Sports" },
            new() { Id = 12, Name = "Supernatural" },
            new() { Id = 13, Name = "Thriller" }
        };
        return Task.FromResult(genres);
    }

    private Anime MapFromKitsu(JsonElement element, int year, string season)
    {
        var attributes = element.GetProperty("attributes");
        var title = attributes.TryGetProperty("canonicalTitle", out var ct) ? ct.GetString() : "Unknown";
        var synopsis = attributes.TryGetProperty("synopsis", out var syn) ? syn.GetString() : "No synopsis available.";
        var coverImage = attributes.TryGetProperty("coverImage", out var ci) &&
                        ci.TryGetProperty("original", out var orig)
            ? orig.GetString()
            : null;

        return new Anime
        {
            KitsuId = element.TryGetProperty("id", out var id) && int.TryParse(id.GetString(), out var parsedId) ? parsedId : 0,
            Title = title,
            Synopsis = synopsis,
            ImageUrl = coverImage,
            Type = attributes.TryGetProperty("format", out var f) ? f.GetString()?.Replace("_", " ") : null,
            Episodes = attributes.TryGetProperty("episodeCount", out var ep) && ep.ValueKind != JsonValueKind.Null ? ep.GetInt32() : (int?)null,
            Rating = attributes.TryGetProperty("averageRating", out var ar) && ar.ValueKind != JsonValueKind.Null ? (decimal?)ar.GetDouble() / 10.0m : null,
            Status = attributes.TryGetProperty("status", out var st) ? st.GetString()?.Replace("_", " ") : null,
            StartDate = attributes.TryGetProperty("startDate", out var sd) && sd.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(sd.GetString() ?? "")
                : (DateTime?)null,
            EndDate = attributes.TryGetProperty("endDate", out var ed) && ed.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(ed.GetString() ?? "")
                : (DateTime?)null,
            SeasonId = 0
        };
    }
}
