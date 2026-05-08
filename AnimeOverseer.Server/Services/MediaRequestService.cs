using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AnimeOverseer.Server.Services;

public class MediaRequestService(SettingsService settings, IHttpClientFactory httpClientFactory)
{
    public async Task<(bool Ok, string Msg)> RequestInSonarrAsync(string title)
    {
        var url = await settings.GetAsync("sonarr.url");
        var key = await settings.GetAsync("sonarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Sonarr is not configured. Go to Settings.");

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var lookupResp = await client.GetAsync($"{url}/api/v3/series/lookup?term={Uri.EscapeDataString(title)}");
        if (!lookupResp.IsSuccessStatusCode)
            return (false, $"Lookup failed: HTTP {(int)lookupResp.StatusCode}");

        var results = await lookupResp.Content.ReadFromJsonAsync<JsonArray>();
        if (results == null || results.Count == 0)
            return (false, $"Not found in Sonarr for \"{title}\"");

        var series = results[0]!.AsObject();
        if (series["id"]?.GetValue<int>() > 0)
            return (false, "Already exists in Sonarr");

        var profileId = await GetFirstProfileIdAsync(client, url);
        if (profileId == null) return (false, "No quality profiles found in Sonarr");

        var rootFolder = await GetFirstRootFolderAsync(client, url);
        if (rootFolder == null) return (false, "No root folders found in Sonarr");

        series["qualityProfileId"] = profileId.Value;
        series["seriesType"] = "anime";
        series["rootFolderPath"] = rootFolder;
        series["monitored"] = true;
        series["addOptions"] = new JsonObject
        {
            ["monitor"] = "all",
            ["searchForMissingEpisodes"] = true
        };

        var addResp = await client.PostAsJsonAsync($"{url}/api/v3/series", series);
        return addResp.IsSuccessStatusCode
            ? (true, "Added to Sonarr")
            : (false, $"Sonarr error: HTTP {(int)addResp.StatusCode}");
    }

    public async Task<(bool Ok, string Msg)> RequestInRadarrAsync(string title)
    {
        var url = await settings.GetAsync("radarr.url");
        var key = await settings.GetAsync("radarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Radarr is not configured. Go to Settings.");

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var lookupResp = await client.GetAsync($"{url}/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}");
        if (!lookupResp.IsSuccessStatusCode)
            return (false, $"Lookup failed: HTTP {(int)lookupResp.StatusCode}");

        var results = await lookupResp.Content.ReadFromJsonAsync<JsonArray>();
        if (results == null || results.Count == 0)
            return (false, $"Not found in Radarr for \"{title}\"");

        var movie = results[0]!.AsObject();
        if (movie["id"]?.GetValue<int>() > 0)
            return (false, "Already exists in Radarr");

        var profileId = await GetFirstProfileIdAsync(client, url);
        if (profileId == null) return (false, "No quality profiles found in Radarr");

        var rootFolder = await GetFirstRootFolderAsync(client, url);
        if (rootFolder == null) return (false, "No root folders found in Radarr");

        movie["qualityProfileId"] = profileId.Value;
        movie["rootFolderPath"] = rootFolder;
        movie["monitored"] = true;
        movie["addOptions"] = new JsonObject
        {
            ["searchForMovie"] = true
        };

        var addResp = await client.PostAsJsonAsync($"{url}/api/v3/movie", movie);
        return addResp.IsSuccessStatusCode
            ? (true, "Added to Radarr")
            : (false, $"Radarr error: HTTP {(int)addResp.StatusCode}");
    }

    private static async Task<int?> GetFirstProfileIdAsync(HttpClient client, string baseUrl)
    {
        var resp = await client.GetAsync($"{baseUrl}/api/v3/qualityprofile");
        if (!resp.IsSuccessStatusCode) return null;
        var arr = await resp.Content.ReadFromJsonAsync<JsonArray>();

        var profile = arr?.Where(p => p["name"]?.GetValue<string>() == "HD-1080p").FirstOrDefault();

        if (profile != null)
        {
            return profile["id"]?.GetValue<int>();
        }

        return arr?.Count > 0 ? arr[0]!["id"]?.GetValue<int>() : null;
    }

    private static async Task<string?> GetFirstRootFolderAsync(HttpClient client, string baseUrl)
    {
        var resp = await client.GetAsync($"{baseUrl}/api/v3/rootfolder");
        if (!resp.IsSuccessStatusCode) return null;
        var arr = await resp.Content.ReadFromJsonAsync<JsonArray>();
        return arr?.Count > 0 ? arr[0]!["path"]?.GetValue<string>() : null;
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
