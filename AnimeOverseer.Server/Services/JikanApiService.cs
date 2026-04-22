using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public class JikanApiService : IAnimeDataSource
{
    private readonly HttpClient _httpClient;

    public JikanApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        var animes = new List<Anime>();
        try
        {
            var response = await _httpClient.GetAsync($"https://api.jikan.moe/v4/seasons/{year}/{season}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                foreach (var anime in data.EnumerateArray())
                    animes.Add(MapFromJikan(anime));
            }
        }
        catch
        {
            // Rate limiting or API errors
        }
        return animes;
    }

    public async Task<List<Anime>> SearchAsync(string query)
    {
        var animes = new List<Anime>();
        try
        {
            var response = await _httpClient.GetAsync($"https://api.jikan.moe/v4/anime?q={Uri.EscapeDataString(query)}&limit=25");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                foreach (var anime in data.EnumerateArray())
                    animes.Add(MapFromJikan(anime));
            }
        }
        catch { }
        return animes;
    }

    public async Task<Anime?> GetByIdAsync(int id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"https://api.jikan.moe/v4/anime/{id}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                return MapFromJikan(data);
            }
        }
        catch { }
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

    internal Anime MapFromJikan(JsonElement element)
    {
        var titleEnglish = element.TryGetProperty("title_english", out var te) && te.ValueKind != JsonValueKind.Null
            ? te.GetString() : null;
        var title = !string.IsNullOrEmpty(titleEnglish)
            ? titleEnglish
            : element.GetProperty("title").GetString() ?? "Unknown";

        var imageUrl = element.TryGetProperty("images", out var images) &&
                       images.TryGetProperty("jpg", out var jpg) &&
                       jpg.TryGetProperty("large_image_url", out var largeUrl)
            ? largeUrl.GetString() : null;

        var genres = element.TryGetProperty("genres", out var genresEl)
            ? genresEl.EnumerateArray()
                .Select(g => g.TryGetProperty("name", out var gn) ? gn.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => new AnimeGenre { Genre = new Genre { Name = n! } })
                .ToList()
            : new List<AnimeGenre>();

        return new Anime
        {
            MALId = element.TryGetProperty("mal_id", out var malId) ? malId.GetInt32() : 0,
            Title = title,
            OriginalTitle = element.TryGetProperty("title", out var jt) && jt.ValueKind != JsonValueKind.Null
                ? jt.GetString() : null,
            Synopsis = element.TryGetProperty("synopsis", out var syn) ? syn.GetString() : "No synopsis available.",
            ImageUrl = imageUrl,
            Type = element.TryGetProperty("type", out var t) ? t.GetString() : null,
            Episodes = element.TryGetProperty("episodes", out var ep) && ep.ValueKind != JsonValueKind.Null
                ? ep.GetInt32() : (int?)null,
            Rating = element.TryGetProperty("score", out var sc) && sc.ValueKind != JsonValueKind.Null
                ? (decimal?)sc.GetDouble() : null,
            Status = element.TryGetProperty("status", out var st) ? st.GetString() : null,
            StartDate = element.TryGetProperty("aired", out var aired) &&
                        aired.TryGetProperty("from", out var from) && from.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(from.GetString() ?? "") : (DateTime?)null,
            EndDate = element.TryGetProperty("aired", out var ended) &&
                      ended.TryGetProperty("to", out var to) && to.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(to.GetString() ?? "") : (DateTime?)null,
            SeasonId = 0,
            AnimeGenres = genres
        };
    }
}
