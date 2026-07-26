using System.Text;
using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

/// <summary>Read-only AniList catalogue client. AniList IDs are external references only.</summary>
public class AniListApiService(HttpClient httpClient, ILogger<AniListApiService> logger)
{
    private const string ApiUrl = "https://graphql.anilist.co";
    private const string MediaFields = @"
        id idMal title { romaji english native } synonyms
        coverImage { large } bannerImage description(asHtml: false) format episodes duration status
        averageScore startDate { year month day } endDate { year month day }
        genres tags { name category isMediaSpoiler }
        relations { edges { relationType node { id idMal title { romaji english native } } } }";

    public async Task<List<Anime>> GetSeasonAnimesAsync(int year, string season, Func<int, int, Task>? onPageFetched = null, CancellationToken ct = default)
    {
        var result = new List<Anime>();
        for (var page = 1; ; page++)
        {
            var data = await QueryAsync(@"query ($page: Int!, $year: Int!, $season: MediaSeason!) {
                Page(page: $page, perPage: 50) { pageInfo { hasNextPage } media(type: ANIME, seasonYear: $year, season: $season, sort: [POPULARITY_DESC]) { " + MediaFields + " } } }",
                new { page, year, season = season.ToUpperInvariant() }, ct);
            var pageData = data.GetProperty("Page");
            var animes = pageData.GetProperty("media").EnumerateArray().Select(MapFromAniList).ToList();
            result.AddRange(animes);
            if (onPageFetched != null) await onPageFetched(page, animes.Count);
            if (!pageData.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) break;
            await Task.Delay(TimeSpan.FromSeconds(2), ct); // stays below the current degraded public limit
        }
        return result;
    }

    public async Task<List<Anime>> GetCurrentAsync(bool upcoming, Func<int, int, Task>? onPageFetched = null, CancellationToken ct = default)
    {
        var status = upcoming ? "NOT_YET_RELEASED" : "RELEASING";
        var result = new List<Anime>();
        for (var page = 1; ; page++)
        {
            var data = await QueryAsync(@"query ($page: Int!, $status: MediaStatus!) {
                Page(page: $page, perPage: 50) { pageInfo { hasNextPage } media(type: ANIME, status: $status, sort: [POPULARITY_DESC]) { " + MediaFields + " } } }",
                new { page, status }, ct);
            var pageData = data.GetProperty("Page");
            var animes = pageData.GetProperty("media").EnumerateArray().Select(MapFromAniList).ToList();
            result.AddRange(animes);
            if (onPageFetched != null) await onPageFetched(page, animes.Count);
            if (!pageData.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) break;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return result;
    }

    public async Task<List<Anime>> SearchAsync(string search, CancellationToken ct = default)
    {
        var data = await QueryAsync("query ($search: String!) { Page(perPage: 25) { media(type: ANIME, search: $search) { " + MediaFields + " } } }", new { search }, ct);
        return data.GetProperty("Page").GetProperty("media").EnumerateArray().Select(MapFromAniList).ToList();
    }

    public async Task<Anime?> GetByAniListIdAsync(int aniListId, CancellationToken ct = default)
    {
        var data = await QueryAsync("query ($id: Int!) { Media(id: $id, type: ANIME) { " + MediaFields + " } }", new { id = aniListId }, ct);
        var media = data.GetProperty("Media");
        return media.ValueKind == JsonValueKind.Null ? null : MapFromAniList(media);
    }

    public async Task<Anime?> GetByMalIdAsync(int malId, CancellationToken ct = default)
    {
        var data = await QueryAsync("query ($id: Int!) { Media(idMal: $id, type: ANIME) { " + MediaFields + " } }", new { id = malId }, ct);
        var media = data.GetProperty("Media");
        return media.ValueKind == JsonValueKind.Null ? null : MapFromAniList(media);
    }

    public async Task<List<(int AniListId, string RelationType)>> GetRelationsAsync(int aniListId, CancellationToken ct = default)
    {
        var data = await QueryAsync("query ($id: Int!) { Media(id: $id, type: ANIME) { relations { edges { relationType node { id } } } } }", new { id = aniListId }, ct);
        return data.GetProperty("Media").GetProperty("relations").GetProperty("edges").EnumerateArray()
            .Select(x => (x.GetProperty("node").GetProperty("id").GetInt32(), GetString(x, "relationType") ?? "Related"))
            .ToList();
    }

    public async Task<List<(Anime Anime, int Rating)>> GetRecommendationsAsync(int aniListId, CancellationToken ct = default)
    {
        var data = await QueryAsync("query ($id: Int!) { Media(id: $id, type: ANIME) { recommendations { edges { node { rating mediaRecommendation { " + MediaFields + " } } } } } }", new { id = aniListId }, ct);
        return data.GetProperty("Media").GetProperty("recommendations").GetProperty("edges").EnumerateArray()
            .Where(edge => edge.GetProperty("node").GetProperty("mediaRecommendation").ValueKind != JsonValueKind.Null)
            .Select(edge =>
            {
                var node = edge.GetProperty("node");
                return (MapFromAniList(node.GetProperty("mediaRecommendation")), GetInt(node, "rating") ?? 0);
            })
            .ToList();
    }

    private async Task<JsonElement> QueryAsync(string query, object variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json")
        };
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"AniList returned {(int)response.StatusCode}: {body}", null, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("errors", out var errors)) throw new HttpRequestException($"AniList GraphQL error: {errors}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    internal static Anime MapFromAniList(JsonElement element)
    {
        var titles = element.GetProperty("title");
        var english = GetString(titles, "english");
        var romaji = GetString(titles, "romaji");
        var native = GetString(titles, "native");
        var anime = new Anime
        {
            AniListId = element.GetProperty("id").GetInt32(),
            MALId = GetInt(element, "idMal"),
            Title = english ?? romaji ?? native ?? "Unknown",
            HasEnglishTitle = !string.IsNullOrWhiteSpace(english),
            OriginalTitle = romaji ?? native,
            AlternativeTitles = new[] { english, romaji, native }
                .Concat(element.TryGetProperty("synonyms", out var synonyms) && synonyms.ValueKind == JsonValueKind.Array
                    ? synonyms.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())
                    : [])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList(),
            Synopsis = GetString(element, "description")?.Replace("<br>", "\n").Replace("<i>", "").Replace("</i>", "").Replace("<b>", "").Replace("</b>", "").Trim(),
            ImageUrl = element.TryGetProperty("coverImage", out var image) ? GetString(image, "large") : null,
            SourceImages = Artwork(element, image),
            Type = GetString(element, "format")?.Replace('_', ' '), Episodes = GetInt(element, "episodes"), Duration = GetInt(element, "duration"),
            Rating = GetInt(element, "averageScore") is int score ? score / 10m : null,
            Status = NormalizeStatus(GetString(element, "status")), StartDate = ParseDate(element, "startDate"), EndDate = ParseDate(element, "endDate")
        };
        if (element.TryGetProperty("genres", out var genres)) foreach (var genre in genres.EnumerateArray()) if (genre.GetString() is { Length: > 0 } name) anime.AnimeGenres.Add(new() { Genre = new() { Name = name } });
        if (element.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateArray())
        {
            if (tag.TryGetProperty("isMediaSpoiler", out var spoiler) && spoiler.GetBoolean()) continue;
            var name = GetString(tag, "name"); if (string.IsNullOrWhiteSpace(name)) continue;
            if (string.Equals(GetString(tag, "category"), "Demographic", StringComparison.OrdinalIgnoreCase)) anime.AnimeDemographics.Add(new() { Demographic = new() { Name = name } });
            else anime.AnimeThemes.Add(new() { Theme = new() { Name = name } });
        }
        return anime;
    }

    private static string? GetString(JsonElement el, string name) => el.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static List<SourceImage> Artwork(JsonElement element, JsonElement cover)
    {
        var images = new List<SourceImage>();
        if (GetString(cover, "large") is { Length: > 0 } poster) images.Add(new() { Source = "AniList", Type = "Poster", Url = poster });
        if (GetString(element, "bannerImage") is { Length: > 0 } banner) images.Add(new() { Source = "AniList", Type = "Banner", Url = banner });
        return images;
    }
    private static int? GetInt(JsonElement el, string name) => el.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;
    private static DateTime? ParseDate(JsonElement el, string name) { if (!el.TryGetProperty(name, out var d) || GetInt(d, "year") is not int y || GetInt(d, "month") is not int m || GetInt(d, "day") is not int day) return null; try { return new DateTime(y, m, day); } catch { return null; } }
    private static string? NormalizeStatus(string? status) => status switch { "RELEASING" => "Currently Airing", "FINISHED" => "Finished", "NOT_YET_RELEASED" => "Not Yet Aired", "CANCELLED" => "Cancelled", "HIATUS" => "Hiatus", _ => status?.Replace('_', ' ') };
}
