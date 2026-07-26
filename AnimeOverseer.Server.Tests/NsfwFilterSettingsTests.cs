using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public class NsfwFilterSettingsTests
{
    [Fact]
    public void Matches_uses_genres_and_themes_case_insensitively()
    {
        var anime = new Anime
        {
            AnimeGenres = [new() { Genre = new() { Name = "Ecchi" } }],
            AnimeThemes = [new() { Theme = new() { Name = "Nudity" } }]
        };

        Assert.True(NsfwFilterSettings.Matches(anime, ["ecchi"], []));
        Assert.True(NsfwFilterSettings.Matches(anime, [], ["nudity"]));
        Assert.False(NsfwFilterSettings.Matches(anime, ["Hentai"], ["Harem"]));
    }

    [Fact]
    public void FromLines_removes_empty_and_duplicate_entries()
    {
        Assert.Equal(["Ecchi", "Hentai"], NsfwFilterSettings.FromLines(" Hentai\r\n\r\nEcchi\nHentai "));
    }
}
