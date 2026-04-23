using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public class AniListApiService : IAnimeDataSource
{
    private readonly HttpClient _httpClient;
    private const string ApiUrl = "https://graphql.anilist.co";

    private const string MediaFields = @"id idMal title { romaji english native } coverImage { large } description format episodes status averageScore startDate { year month day } genres";

    public AniListApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private Task<HttpResponseMessage> PostGraphQlAsync(string graphqlQuery)
    {
        var payload = JsonSerializer.Serialize(new { query = graphqlQuery });
        return _httpClient.PostAsync(ApiUrl, new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
    }

    public async Task<List<Anime>> GetSeasonAnimes(int year, string season, bool forceRefresh = false)
    {
        var query = $@"{{ Page(perPage: 50) {{ media(season: {season.ToUpper()}, seasonYear: {year}, type: ANIME) {{ {MediaFields} }} }} }}";
        var animes = new List<Anime>();
        try
        {
            var response = await PostGraphQlAsync(query);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media");
                foreach (var anime in data.EnumerateArray())
                    animes.Add(MapFromAniList(anime));
            }
        }
        catch { }
        return animes;
    }

    public async Task<List<Anime>> SearchAsync(string query)
    {
        var escaped = query.Replace("\"", "\\\"");
        var graphqlQuery = $@"{{ Page(perPage: 25) {{ media(search: ""{escaped}"", type: ANIME) {{ {MediaFields} }} }} }}";
        var animes = new List<Anime>();
        try
        {
            var response = await PostGraphQlAsync(graphqlQuery);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media");
                foreach (var anime in data.EnumerateArray())
                    animes.Add(MapFromAniList(anime));
            }
        }
        catch { }
        return animes;
    }

    public async Task<Anime?> GetByIdAsync(int id)
    {
        var query = $@"{{ Media(id: {id}, type: ANIME) {{ {MediaFields} }} }}";
        try
        {
            var response = await PostGraphQlAsync(query);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data").GetProperty("Media");
                return MapFromAniList(data);
            }
        }
        catch { }
        return null;
    }

    public async Task<Anime?> GetByMalIdAsync(int malId)
    {
        var query = $@"{{ Media(idMal: {malId}, type: ANIME) {{ {MediaFields} }} }}";
        try
        {
            var response = await PostGraphQlAsync(query);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data").GetProperty("Media");
                return MapFromAniList(data);
            }
        }
        catch { }
        return null;
    }

    public Task<List<Genre>> GetAllGenresAsync()
    {
        return Task.FromResult(new List<Genre>
        {
            new() { Id = 1, Name = "Action" }, new() { Id = 2, Name = "Adventure" },
            new() { Id = 3, Name = "Comedy" }, new() { Id = 4, Name = "Drama" },
            new() { Id = 5, Name = "Fantasy" }, new() { Id = 6, Name = "Horror" },
            new() { Id = 7, Name = "Mystery" }, new() { Id = 8, Name = "Romance" },
            new() { Id = 9, Name = "Sci-Fi" }, new() { Id = 10, Name = "Slice of Life" },
            new() { Id = 11, Name = "Sports" }, new() { Id = 12, Name = "Supernatural" },
            new() { Id = 13, Name = "Thriller" }
        });
    }

    private static Anime MapFromAniList(JsonElement element)
    {
        var titleObj = element.GetProperty("title");
        var romajiTitle = titleObj.TryGetProperty("romaji", out var rt) && rt.ValueKind != JsonValueKind.Null ? rt.GetString() : null;
        var englishTitle = titleObj.TryGetProperty("english", out var et) && et.ValueKind != JsonValueKind.Null ? et.GetString() : null;
        var nativeTitle = titleObj.TryGetProperty("native", out var nt) && nt.ValueKind != JsonValueKind.Null ? nt.GetString() : null;

        var description = element.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null
            ? desc.GetString()
                ?.Replace("<br><br>", "\n").Replace("<br>", "\n")
                .Replace("<i>", "").Replace("</i>", "")
                .Replace("<b>", "").Replace("</b>", "")
                .Trim()
            : null;

        return new Anime
        {
            AniListId = element.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null ? id.GetInt32() : null,
            MALId = element.TryGetProperty("idMal", out var malId) && malId.ValueKind != JsonValueKind.Null ? malId.GetInt32() : null,
            Title = englishTitle ?? romajiTitle ?? nativeTitle ?? "Unknown",
            OriginalTitle = romajiTitle ?? nativeTitle,
            Synopsis = description,
            ImageUrl = element.TryGetProperty("coverImage", out var ci) && ci.TryGetProperty("large", out var large) && large.ValueKind != JsonValueKind.Null ? large.GetString() : null,
            Type = element.TryGetProperty("format", out var f) && f.ValueKind != JsonValueKind.Null ? f.GetString()?.Replace("_", " ") : null,
            Episodes = element.TryGetProperty("episodes", out var ep) && ep.ValueKind != JsonValueKind.Null ? ep.GetInt32() : null,
            Rating = element.TryGetProperty("averageScore", out var sc) && sc.ValueKind != JsonValueKind.Null ? (decimal?)sc.GetDouble() / 10.0m : null,
            Status = element.TryGetProperty("status", out var st) && st.ValueKind != JsonValueKind.Null ? st.GetString()?.Replace("_", " ") : null,
            StartDate = ParseAniListDate(element),
            SeasonId = 0,
            AnimeGenres = ParseGenres(element)
        };
    }

    private static List<AnimeGenre> ParseGenres(JsonElement element)
    {
        var result = new List<AnimeGenre>();
        if (!element.TryGetProperty("genres", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var g in arr.EnumerateArray())
        {
            var name = g.GetString();
            if (!string.IsNullOrEmpty(name))
                result.Add(new AnimeGenre { Genre = new Genre { Name = name } });
        }
        return result;
    }

    private static DateTime? ParseAniListDate(JsonElement element)
    {
        if (!element.TryGetProperty("startDate", out var sd)) return null;
        var hasY = sd.TryGetProperty("year", out var yEl) && yEl.ValueKind != JsonValueKind.Null;
        var hasM = sd.TryGetProperty("month", out var mEl) && mEl.ValueKind != JsonValueKind.Null;
        var hasD = sd.TryGetProperty("day", out var dEl) && dEl.ValueKind != JsonValueKind.Null;
        if (!hasY || !hasM || !hasD) return null;
        try { return new DateTime(yEl.GetInt32(), mEl.GetInt32(), dEl.GetInt32()); }
        catch { return null; }
    }
}
