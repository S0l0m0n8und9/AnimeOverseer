using System.Net.Http.Headers;
using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

/// <summary>Optional secondary source. Configure MyAnimeList:ClientId outside source control.</summary>
public class MyAnimeListApiService(HttpClient httpClient, IConfiguration configuration)
{
    private const string BaseUrl = "https://api.myanimelist.net/v2";
    private string ClientId => configuration["MyAnimeList:ClientId"] ?? "";
    private const string Fields = "id,title,main_picture,alternative_titles,synopsis,media_type,status,num_episodes,mean,start_date,end_date,genres";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public async Task<List<Anime>> GetSeasonAsync(int year, string season, CancellationToken ct = default)
    {
        EnsureConfigured();
        var result = new List<Anime>();
        var url = $"{BaseUrl}/anime/season/{year}/{season}?limit=100&fields={Fields}";
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var doc = await GetAsync(url, ct);
            result.AddRange(doc.RootElement.GetProperty("data").EnumerateArray().Select(x => Map(x.GetProperty("node"))));
            url = doc.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) ? next.GetString() ?? "" : "";
        }
        return result;
    }

    public async Task<Anime?> GetByIdAsync(int malId, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var doc = await GetAsync($"{BaseUrl}/anime/{malId}?fields={Fields}", ct);
        return Map(doc.RootElement);
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MAL-CLIENT-ID", ClientId);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static Anime Map(JsonElement x) => new()
    {
        MALId = x.GetProperty("id").GetInt32(), Title = x.GetProperty("title").GetString() ?? "Unknown",
        OriginalTitle = x.TryGetProperty("alternative_titles", out var titles) && titles.TryGetProperty("ja", out var ja) ? ja.GetString() : null,
        Synopsis = Get(x, "synopsis"), ImageUrl = x.TryGetProperty("main_picture", out var pic) ? Get(pic, "large") ?? Get(pic, "medium") : null,
        Type = Get(x, "media_type")?.ToUpperInvariant() switch { "TV" => "TV", var type => type?.Replace('_', ' ') },
        Status = Get(x, "status")?.Replace('_', ' '), Episodes = Int(x, "num_episodes"),
        Rating = x.TryGetProperty("mean", out var mean) && mean.ValueKind != JsonValueKind.Null ? mean.GetDecimal() : null,
        StartDate = Date(x, "start_date"), EndDate = Date(x, "end_date"),
        AnimeGenres = x.TryGetProperty("genres", out var genres) ? genres.EnumerateArray().Select(g => new AnimeGenre { Genre = new Genre { Name = Get(g, "name") ?? "" } }).Where(g => g.Genre.Name.Length > 0).ToList() : []
    };
    private static string? Get(JsonElement x, string name) => x.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
    private static int? Int(JsonElement x, string name) => x.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetInt32() : null;
    private static DateTime? Date(JsonElement x, string name) => DateTime.TryParse(Get(x, name), out var value) ? value : null;
    private void EnsureConfigured() { if (!IsConfigured) throw new InvalidOperationException("MyAnimeList:ClientId is not configured."); }
}
