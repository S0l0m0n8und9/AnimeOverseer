using System.Net.Http.Json;
using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public class AniListApiService : IAnimeDataSource
{
    private readonly HttpClient _httpClient;
    private const string ApiUrl = "https://graphql.anilist.co";

    public AniListApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        var query = $@"
            {{
                Page(perPage: 50) {{
                    media(season: {season.ToUpper()}, seasonYear: {year}, type: ANIME) {{
                        id
                        title {{ english native }}
                        coverImage {{ large }}
                        description
                        format
                        episodes
                        status
                        averageScore
                        startDate {{ year month day }}
                        genres
                    }}
                }}
            }}";

        var animes = new List<Anime>();
        try
        {
            var response = await _httpClient.PostAsync(ApiUrl, new StringContent(query, System.Text.Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("Page")
                    .GetProperty("media");

                foreach (var anime in data.EnumerateArray())
                {
                    animes.Add(MapFromAniList(anime, year, season));
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
        var escapedQuery = Uri.EscapeDataString(query);
        var searchQuery = $"{{\n  Page(perPage: 25) {{\n    media(search: \"{escapedQuery}\", type: ANIME) {{\n      id\n      title {{ english native }}\n      coverImage {{ large }}\n      description\n      format\n      episodes\n      status\n      averageScore\n      startDate {{ year month day }}\n      genres\n    }}\n  }}\n}}";

        var animes = new List<Anime>();
        try
        {
            var response = await _httpClient.PostAsync(ApiUrl, new StringContent(searchQuery, System.Text.Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("Page")
                    .GetProperty("media");

                foreach (var anime in data.EnumerateArray())
                {
                    animes.Add(MapFromAniList(anime, 0, ""));
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
        var query = $@"
            {{
                media(id: {id}, type: ANIME) {{
                    id
                    title {{ english native }}
                    coverImage {{ large }}
                    description
                    format
                    episodes
                    status
                    averageScore
                    startDate {{ year month day }}
                    genres
                }}
            }}";

        try
        {
            var response = await _httpClient.PostAsync(ApiUrl, new StringContent(query, System.Text.Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("media");
                return MapFromAniList(data, 0, "");
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

    private Anime MapFromAniList(JsonElement element, int year, string season)
    {
        var titleObj = element.GetProperty("title");
        var englishTitle = titleObj.TryGetProperty("english", out var et) && et.ValueKind != JsonValueKind.Null ? et.GetString() : "Unknown";
        var nativeTitle = titleObj.TryGetProperty("native", out var nt) ? nt.GetString() : null;

        var coverImage = element.TryGetProperty("coverImage", out var ci) &&
                        ci.TryGetProperty("large", out var large)
            ? large.GetString()
            : null;

        var description = element.TryGetProperty("description", out var desc) ? desc.GetString()?.Replace("<br><br>", "\n")?.Replace("<i>", "").Replace("</i>", "") ?? "No synopsis available." : "No synopsis available.";

        return new Anime
        {
            AniListId = element.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Title = englishTitle,
            OriginalTitle = nativeTitle,
            Synopsis = description,
            ImageUrl = coverImage,
            Type = element.TryGetProperty("format", out var f) ? f.GetString()?.Replace("_", " ") : null,
            Episodes = element.TryGetProperty("episodes", out var ep) && ep.ValueKind != JsonValueKind.Null ? ep.GetInt32() : (int?)null,
            Rating = element.TryGetProperty("averageScore", out var sc) && sc.ValueKind != JsonValueKind.Null ? (decimal?)sc.GetDouble() / 10.0m : null,
            Status = element.TryGetProperty("status", out var st) ? st.GetString()?.Replace("_", " ") : null,
            StartDate = ParseAniListStartDate(element),
            SeasonId = 0
        };
    }

    private DateTime? ParseAniListStartDate(JsonElement element)
    {
        if (!element.TryGetProperty("startDate", out var sd))
            return null;

        int y = 0, m = 0, d = 0;
        bool hasY = sd.TryGetProperty("year", out var yEl) && yEl.ValueKind != JsonValueKind.Null;
        bool hasM = sd.TryGetProperty("month", out var mEl) && mEl.ValueKind != JsonValueKind.Null;
        bool hasD = sd.TryGetProperty("day", out var dEl) && dEl.ValueKind != JsonValueKind.Null;

        if (hasY) y = yEl.GetInt32();
        if (hasM) m = mEl.GetInt32();
        if (hasD) d = dEl.GetInt32();

        return hasY || hasM || hasD ? new DateTime(y, m, d) : null;
    }
}
