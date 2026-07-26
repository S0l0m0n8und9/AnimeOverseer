using System.Text.Json;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class NsfwFilterSettings
{
    public const string GenreSettingKey = "library.nsfwGenres";
    public const string ThemeSettingKey = "library.nsfwThemes";

    public static readonly string[] DefaultGenres = ["Hentai", "Erotica", "Ecchi"];

    public static readonly string[] DefaultThemes =
    [
        "Ahegao", "Futanari", "Harem", "Masturbation", "Netorare",
        "Nudity", "Prostitution", "Reverse Harem", "Sexual Abuse", "Sexual Content"
    ];

    public static List<string> Read(string? json, IEnumerable<string> defaults)
    {
        if (string.IsNullOrWhiteSpace(json)) return Normalize(defaults);

        try
        {
            return Normalize(JsonSerializer.Deserialize<List<string>>(json) ?? defaults);
        }
        catch (JsonException)
        {
            return Normalize(defaults);
        }
    }

    public static List<string> FromLines(string value) => Normalize(value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    public static string ToLines(IEnumerable<string> values) => string.Join(Environment.NewLine, Normalize(values));

    public static bool Matches(Anime anime, IEnumerable<string> genres, IEnumerable<string> themes)
    {
        var genreTerms = Normalize(genres);
        var themeTerms = Normalize(themes);

        return anime.AnimeGenres.Any(x => MatchesAny(x.Genre?.Name, genreTerms)) ||
               anime.AnimeThemes.Any(x => MatchesAny(x.Theme?.Name, themeTerms));
    }

    private static List<string> Normalize(IEnumerable<string> values) => values
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool MatchesAny(string? tag, IEnumerable<string> terms) =>
        !string.IsNullOrWhiteSpace(tag) && terms.Any(term => tag.Equals(term, StringComparison.OrdinalIgnoreCase));
}
