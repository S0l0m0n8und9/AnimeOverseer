namespace AnimeOverseer.Server.Services;

public static class OAuthCallbackUrl
{
    public static string? Build(string? applicationUrl, string provider)
    {
        if (!Uri.TryCreate(applicationUrl?.Trim(), UriKind.Absolute, out var uri)) return null;
        var baseUrl = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
        return $"{baseUrl}/api/oauth/{provider}/callback";
    }
}
