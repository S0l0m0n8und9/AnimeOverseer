using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AnimeOverseer.Server.Services;

public class MediaLibraryService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory)
{
    private HashSet<string> _sonarrTitles = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _radarrTitles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefreshed = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsInSonarr(string? title) => !string.IsNullOrWhiteSpace(title) && _sonarrTitles.Contains(title.Trim());
    public bool IsInRadarr(string? title) => !string.IsNullOrWhiteSpace(title) && _radarrTitles.Contains(title.Trim());

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
