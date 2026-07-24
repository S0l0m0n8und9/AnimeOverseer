using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeOverseer.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AnimeOverseer.Server.Controllers;

[ApiController]
[Route("api/oauth")]
public class OAuthController(SettingsService settings, IHttpClientFactory httpClientFactory) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, OAuthProvider> Providers =
        new Dictionary<string, OAuthProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["myanimelist"] = new(
                "myanimelist",
                "https://myanimelist.net/v1/oauth2/authorize",
                "https://myanimelist.net/v1/oauth2/token",
                UsesPlainPkce: true),
            ["animeschedule"] = new(
                "animeschedule",
                "https://animeschedule.net/api/v3/oauth2/authorize",
                "https://animeschedule.net/api/v3/oauth2/token",
                UsesPlainPkce: false)
        };

    [HttpGet("{provider}/start")]
    public async Task<IActionResult> Start(string provider)
    {
        if (!Providers.TryGetValue(provider, out var definition)) return NotFound();

        var clientId = await settings.GetAsync($"{definition.Key}.clientId");
        var redirectUri = OAuthCallbackUrl.Build(await settings.GetAsync("oauth.applicationUrl"), definition.Key)
            ?? await settings.GetAsync($"{definition.Key}.redirectUri");
        if (string.IsNullOrWhiteSpace(clientId) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            return BadRequest("Save a client ID and an absolute callback URL before connecting.");

        var state = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = ToBase64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = definition.UsesPlainPkce
            ? verifier
            : ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        await settings.SetAsync($"oauth.{definition.Key}.pending", JsonSerializer.Serialize(new PendingAuthorization(state, verifier, DateTime.UtcNow)));

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = challenge
        };
        if (!definition.UsesPlainPkce) query["code_challenge_method"] = "S256";
        var scopes = await settings.GetAsync($"{definition.Key}.scopes");
        if (!string.IsNullOrWhiteSpace(scopes)) query["scope"] = scopes;
        return Redirect(QueryHelpers.AddQueryString(definition.AuthorizationEndpoint, query));
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription)
    {
        if (!Providers.TryGetValue(provider, out var definition)) return NotFound();
        if (!string.IsNullOrWhiteSpace(error)) return ResultPage(false, $"Authorization was declined: {errorDescription ?? error}");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return ResultPage(false, "The authorization response did not include a code and state.");

        var pendingJson = await settings.GetAsync($"oauth.{definition.Key}.pending");
        PendingAuthorization? pending;
        try { pending = JsonSerializer.Deserialize<PendingAuthorization>(pendingJson ?? ""); }
        catch (JsonException) { pending = null; }
        if (pending is null || pending.CreatedAt < DateTime.UtcNow.AddMinutes(-15) || !FixedTimeEquals(pending.State, state))
            return ResultPage(false, "This authorization request is no longer valid. Return to Settings and try again.");

        try
        {
            var clientId = await settings.GetAsync($"{definition.Key}.clientId") ?? "";
            var clientSecret = await settings.GetAsync($"{definition.Key}.clientSecret") ?? "";
            var redirectUri = OAuthCallbackUrl.Build(await settings.GetAsync("oauth.applicationUrl"), definition.Key)
                ?? await settings.GetAsync($"{definition.Key}.redirectUri") ?? "";
            using var client = httpClientFactory.CreateClient();
            using var response = await client.PostAsync(definition.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = pending.Verifier,
                ["client_secret"] = clientSecret
            }.Where(x => !string.IsNullOrWhiteSpace(x.Value))), HttpContext.RequestAborted);
            var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                var detail = TokenError(body);
                return ResultPage(false, $"Token exchange failed (HTTP {(int)response.StatusCode}){(string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}")}");
            }

            using var token = JsonDocument.Parse(body);
            if (!token.RootElement.TryGetProperty("access_token", out var accessToken)) return ResultPage(false, "The provider did not return an access token.");
            await settings.SetAsync($"{definition.Key}.accessToken", accessToken.GetString());
            await settings.SetAsync($"{definition.Key}.refreshToken", token.RootElement.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null);
            if (token.RootElement.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds))
                await settings.SetAsync($"{definition.Key}.tokenExpiresAt", DateTime.UtcNow.AddSeconds(seconds).ToString("O"));
            await settings.SetAsync($"oauth.{definition.Key}.pending", null);
            return ResultPage(true, $"{definition.DisplayName} is connected. You can close this page and return to Settings.");
        }
        catch (Exception)
        {
            return ResultPage(false, "The token exchange could not be completed. Check the saved callback URL and application credentials.");
        }
    }

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ContentResult ResultPage(bool success, string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        var colour = success ? "#198754" : "#b42318";
        return new ContentResult { ContentType = "text/html; charset=utf-8", Content = $"<!doctype html><title>AnimeOverseer connection</title><main style='font-family:system-ui;max-width:42rem;margin:4rem auto;padding:1rem'><h1 style='color:{colour}'>AnimeOverseer</h1><p>{safe}</p><p><a href='/settings'>Return to Settings</a></p></main>" };
    }

    private static string? TokenError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return root.TryGetProperty("error_description", out var description) ? description.GetString()
                : root.TryGetProperty("error", out var error) ? error.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record OAuthProvider(string Key, string AuthorizationEndpoint, string TokenEndpoint, bool UsesPlainPkce)
    {
        public string DisplayName => Key == "myanimelist" ? "MyAnimeList" : "AnimeSchedule";
    }

    private sealed record PendingAuthorization(string State, string Verifier, DateTime CreatedAt);
}
