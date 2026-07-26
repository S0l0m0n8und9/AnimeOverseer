using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AnimeOverseer.Server.Services;

public record MediaLookupResult(
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    bool AlreadyExists,
    JsonObject RawData
);

public class MediaRequestService(SettingsService settings, IHttpClientFactory httpClientFactory)
{
    public async Task<(bool Ok, string Error, List<MediaLookupResult>? Results)> LookupSonarrAsync(string title)
    {
        var url = await settings.GetAsync("sonarr.url");
        var key = await settings.GetAsync("sonarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Sonarr is not configured. Go to Settings.", null);

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var lookupResp = await client.GetAsync($"{url}/api/v3/series/lookup?term={Uri.EscapeDataString(title)}");
        if (!lookupResp.IsSuccessStatusCode)
            return (false, $"Lookup failed: HTTP {(int)lookupResp.StatusCode}", null);

        var results = await lookupResp.Content.ReadFromJsonAsync<JsonArray>();
        if (results == null || results.Count == 0)
            return (false, $"Not found in Sonarr for \"{title}\"", null);

        var list = results
            .OfType<JsonObject>()
            .Select(s => new MediaLookupResult(
                s["title"]?.GetValue<string>() ?? "",
                s["year"]?.GetValue<int>(),
                s["overview"]?.GetValue<string>(),
                s["images"]?.AsArray()
                    .OfType<JsonObject>()
                    .FirstOrDefault(i => i["coverType"]?.GetValue<string>() == "poster")
                    ?["remoteUrl"]?.GetValue<string>(),
                s["id"]?.GetValue<int>() > 0,
                s
            ))
            .ToList();

        return (true, "", list);
    }

    public async Task<(bool Ok, string Error, List<MediaLookupResult>? Results)> LookupRadarrAsync(string title)
    {
        var url = await settings.GetAsync("radarr.url");
        var key = await settings.GetAsync("radarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Radarr is not configured. Go to Settings.", null);

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var lookupResp = await client.GetAsync($"{url}/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}");
        if (!lookupResp.IsSuccessStatusCode)
            return (false, $"Lookup failed: HTTP {(int)lookupResp.StatusCode}", null);

        var results = await lookupResp.Content.ReadFromJsonAsync<JsonArray>();
        if (results == null || results.Count == 0)
            return (false, $"Not found in Radarr for \"{title}\"", null);

        var list = results
            .OfType<JsonObject>()
            .Select(m => new MediaLookupResult(
                m["title"]?.GetValue<string>() ?? "",
                m["year"]?.GetValue<int>(),
                m["overview"]?.GetValue<string>(),
                m["images"]?.AsArray()
                    .OfType<JsonObject>()
                    .FirstOrDefault(i => i["coverType"]?.GetValue<string>() == "poster")
                    ?["remoteUrl"]?.GetValue<string>(),
                m["id"]?.GetValue<int>() > 0,
                m
            ))
            .ToList();

        return (true, "", list);
    }

    public async Task<(bool Ok, string Msg)> AddToSonarrAsync(JsonObject series)
    {
        var url = await settings.GetAsync("sonarr.url");
        var key = await settings.GetAsync("sonarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Sonarr is not configured. Go to Settings.");

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        if (series["id"]?.GetValue<int>() > 0)
            return (false, "Already exists in Sonarr");

        var profileId = await GetProfileIdAsync(client, url, "sonarr.qualityProfileId");
        if (profileId == null) return (false, "No quality profiles found in Sonarr");

        var rootFolder = await GetRootFolderAsync(client, url, "sonarr.rootFolderPath");
        if (rootFolder == null) return (false, "No root folders found in Sonarr");

        series["qualityProfileId"] = profileId.Value;
        series["seriesType"] = await GetStringSettingAsync("sonarr.seriesType", "anime");
        series["rootFolderPath"] = rootFolder;
        series["monitored"] = await GetBoolSettingAsync("sonarr.monitored", true);
        series["seasonFolder"] = await GetBoolSettingAsync("sonarr.seasonFolder", true);
        series["addOptions"] = new JsonObject
        {
            ["monitor"] = await GetStringSettingAsync("sonarr.monitor", "all"),
            ["searchForMissingEpisodes"] = await GetBoolSettingAsync("sonarr.searchForMissingEpisodes", true)
        };

        var addResp = await client.PostAsJsonAsync($"{url}/api/v3/series", series);
        return addResp.IsSuccessStatusCode
            ? (true, "Added to Sonarr")
            : (false, $"Sonarr error: HTTP {(int)addResp.StatusCode}");
    }

    public async Task<(bool Ok, string Msg)> AddToRadarrAsync(JsonObject movie)
    {
        var url = await settings.GetAsync("radarr.url");
        var key = await settings.GetAsync("radarr.apikey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Radarr is not configured. Go to Settings.");

        url = NormalizeUrl(url);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        if (movie["id"]?.GetValue<int>() > 0)
            return (false, "Already exists in Radarr");

        var profileId = await GetProfileIdAsync(client, url, "radarr.qualityProfileId");
        if (profileId == null) return (false, "No quality profiles found in Radarr");

        var rootFolder = await GetRootFolderAsync(client, url, "radarr.rootFolderPath");
        if (rootFolder == null) return (false, "No root folders found in Radarr");

        movie["qualityProfileId"] = profileId.Value;
        movie["rootFolderPath"] = rootFolder;
        movie["monitored"] = await GetBoolSettingAsync("radarr.monitored", true);
        movie["addOptions"] = new JsonObject
        {
            ["searchForMovie"] = await GetBoolSettingAsync("radarr.searchForMovie", true)
        };

        var addResp = await client.PostAsJsonAsync($"{url}/api/v3/movie", movie);
        return addResp.IsSuccessStatusCode
            ? (true, "Added to Radarr")
            : (false, $"Radarr error: HTTP {(int)addResp.StatusCode}");
    }

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

        var profileId = await GetProfileIdAsync(client, url, "sonarr.qualityProfileId");
        if (profileId == null) return (false, "No quality profiles found in Sonarr");

        var rootFolder = await GetRootFolderAsync(client, url, "sonarr.rootFolderPath");
        if (rootFolder == null) return (false, "No root folders found in Sonarr");

        series["qualityProfileId"] = profileId.Value;
        series["seriesType"] = await GetStringSettingAsync("sonarr.seriesType", "anime");
        series["rootFolderPath"] = rootFolder;
        series["monitored"] = await GetBoolSettingAsync("sonarr.monitored", true);
        series["seasonFolder"] = await GetBoolSettingAsync("sonarr.seasonFolder", true);
        series["addOptions"] = new JsonObject
        {
            ["monitor"] = await GetStringSettingAsync("sonarr.monitor", "all"),
            ["searchForMissingEpisodes"] = await GetBoolSettingAsync("sonarr.searchForMissingEpisodes", true)
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

        var profileId = await GetProfileIdAsync(client, url, "radarr.qualityProfileId");
        if (profileId == null) return (false, "No quality profiles found in Radarr");

        var rootFolder = await GetRootFolderAsync(client, url, "radarr.rootFolderPath");
        if (rootFolder == null) return (false, "No root folders found in Radarr");

        movie["qualityProfileId"] = profileId.Value;
        movie["rootFolderPath"] = rootFolder;
        movie["monitored"] = await GetBoolSettingAsync("radarr.monitored", true);
        movie["addOptions"] = new JsonObject
        {
            ["searchForMovie"] = await GetBoolSettingAsync("radarr.searchForMovie", true)
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

    private async Task<int?> GetProfileIdAsync(HttpClient client, string baseUrl, string settingKey)
    {
        var configured = await settings.GetAsync(settingKey);
        return int.TryParse(configured, out var profileId) && profileId > 0
            ? profileId
            : await GetFirstProfileIdAsync(client, baseUrl);
    }

    private async Task<string?> GetRootFolderAsync(HttpClient client, string baseUrl, string settingKey)
    {
        var configured = await settings.GetAsync(settingKey);
        return string.IsNullOrWhiteSpace(configured)
            ? await GetFirstRootFolderAsync(client, baseUrl)
            : configured.Trim();
    }

    private async Task<string> GetStringSettingAsync(string key, string defaultValue)
    {
        var value = await settings.GetAsync(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        var value = await settings.GetAsync(key);
        return bool.TryParse(value, out var result) ? result : defaultValue;
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
