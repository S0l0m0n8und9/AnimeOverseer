using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public class MediaLibraryService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory)
{
    private HashSet<string> _sonarrTitles = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _radarrTitles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefreshed = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsInSonarr(string? title) => !string.IsNullOrWhiteSpace(title) && _sonarrTitles.Contains(title.Trim());
    public bool IsInSonarr(Anime anime)
    {
        if (anime == null) return false;

        // Check exact matches first (existing logic)
        if (!string.IsNullOrWhiteSpace(anime.Title) && _sonarrTitles.Contains(anime.Title.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(anime.OriginalTitle) && _sonarrTitles.Contains(anime.OriginalTitle.Trim()))
            return true;

        // Check enhanced matching for variations (like "Title" vs "Title Season 2")
        var animeTitles = new List<string>();
        if (!string.IsNullOrWhiteSpace(anime.Title)) animeTitles.Add(anime.Title.Trim());
        if (!string.IsNullOrWhiteSpace(anime.OriginalTitle)) animeTitles.Add(anime.OriginalTitle.Trim());

        foreach (var sonarrTitle in _sonarrTitles)
        {
            foreach (var animeTitle in animeTitles)
            {
                if (TitlesMatch(animeTitle, sonarrTitle))
                    return true;
            }
        }

        return false;
    }

    public bool IsInRadarr(string? title) => !string.IsNullOrWhiteSpace(title) && _radarrTitles.Contains(title.Trim());
    public bool IsInRadarr(Anime anime)
    {
        if (anime == null) return false;

        // Check exact matches first (existing logic)
        if (!string.IsNullOrWhiteSpace(anime.Title) && _radarrTitles.Contains(anime.Title.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(anime.OriginalTitle) && _radarrTitles.Contains(anime.OriginalTitle.Trim()))
            return true;

        // Check enhanced matching for variations (like "Title" vs "Title Season 2")
        var animeTitles = new List<string>();
        if (!string.IsNullOrWhiteSpace(anime.Title)) animeTitles.Add(anime.Title.Trim());
        if (!string.IsNullOrWhiteSpace(anime.OriginalTitle)) animeTitles.Add(anime.OriginalTitle.Trim());

        foreach (var radarrTitle in _radarrTitles)
        {
            foreach (var animeTitle in animeTitles)
            {
                if (TitlesMatch(animeTitle, radarrTitle))
                    return true;
            }
        }

        return false;
    }


    private bool TitlesMatch(string title1, string title2)
    {
        if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
            return false;

        var t1 = title1.Trim();
        var t2 = title2.Trim();

        // If one contains the other, they might be matches (e.g., "Frieren" vs "Frieren Season 2")
        if (t1.Contains(t2, StringComparison.OrdinalIgnoreCase) ||
            t2.Contains(t1, StringComparison.OrdinalIgnoreCase))
        {
            // Additional check: see if the difference is just season/episode indicators
            var shorter = t1.Length < t2.Length ? t1 : t2;
            var longer = t1.Length < t2.Length ? t2 : t1;

            // Check if longer title is just shorter title plus common season indicators
            var trimmedLonger = longer.Trim();
            var trimmedShorter = shorter.Trim();

            if (trimmedLonger.StartsWith(trimmedShorter, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = trimmedLonger.Substring(trimmedShorter.Length).Trim();
                // Check if remainder looks like a season indicator
                if (IsSeasonIndicator(remainder))
                    return true;
            }

            // Also check the reverse (though less common)
            if (trimmedShorter.StartsWith(trimmedLonger, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = trimmedShorter.Substring(trimmedLonger.Length).Trim();
                if (IsSeasonIndicator(remainder))
                    return true;
            }

            return true; // Basic contains match
        }

        return false;
    }

    private bool IsSeasonIndicator(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        // Common season indicators
        var seasonPatterns = new[]
        {
            @"season\s*\d+",           // "season 2", "season10"
            @"\d+nd\s*season",         // "2nd season"
            @"\d+rd\s*season",         // "3rd season"
            @"\d+th\s*season",         // "4th season", etc.
            @"s\d+",                   // "s2", "s10"
            @"\s*\d+",                 // Just a space and number at end (like "Title 2")
        };

        return seasonPatterns.Any(p => System.Text.RegularExpressions.Regex.IsMatch(trimmed, p,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    public async Task EnsureRefreshedAsync()
    {
        if ((DateTime.UtcNow - _lastRefreshed).TotalMinutes < 10) return;
        await _lock.WaitAsync();
        try
        {
            if ((DateTime.UtcNow - _lastRefreshed).TotalMinutes < 10) return;
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            await Task.WhenAll(RefreshSonarrAsync(settings), RefreshRadarrAsync(settings));
            _lastRefreshed = DateTime.UtcNow;
        }
        finally { _lock.Release(); }
    }

    public void Invalidate() => _lastRefreshed = DateTime.MinValue;

    private async Task RefreshSonarrAsync(SettingsService settings)
    {
        try
        {
            var url = await settings.GetAsync("sonarr.url");
            var key = await settings.GetAsync("sonarr.apikey");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;

            url = NormalizeUrl(url);
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("X-Api-Key", key);

            var resp = await client.GetAsync($"{url}/api/v3/series");
            if (!resp.IsSuccessStatusCode) return;

            var arr = await resp.Content.ReadFromJsonAsync<JsonArray>();
            if (arr == null) return;

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arr.OfType<JsonObject>())
            {
                var t = item["title"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(t)) titles.Add(t.Trim());
            }
            _sonarrTitles = titles;
        }
        catch { /* silently ignore — indicator simply won't show if Sonarr is unreachable */ }
    }

    private async Task RefreshRadarrAsync(SettingsService settings)
    {
        try
        {
            var url = await settings.GetAsync("radarr.url");
            var key = await settings.GetAsync("radarr.apikey");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;

            url = NormalizeUrl(url);
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("X-Api-Key", key);

            var resp = await client.GetAsync($"{url}/api/v3/movie");
            if (!resp.IsSuccessStatusCode) return;

            var arr = await resp.Content.ReadFromJsonAsync<JsonArray>();
            if (arr == null) return;

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arr.OfType<JsonObject>())
            {
                var t = item["title"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(t)) titles.Add(t.Trim());
            }
            _radarrTitles = titles;
        }
        catch { /* silently ignore */ }
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        return url;
    }
}
