using AnimeOverseer.Server.Services;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public class MediaLibraryTitleMatchingTests
{
    [Theory]
    [InlineData("K-On!", "K On")]
    [InlineData("Frieren: Beyond Journey's End", "Frieren Beyond Journeys End")]
    [InlineData("Frieren", "Frieren Season 2")]
    [InlineData("Frieren", "Frieren 2nd Season")]
    [InlineData("Frieren", "Frieren S2")]
    public void Matches_equivalent_titles_and_explicit_season_suffixes(string first, string second) =>
        Assert.True(MediaLibraryService.TitlesMatch(first, second));

    [Theory]
    [InlineData("Blue Lock", "Blue Lock: Episode Nagi")]
    [InlineData("Fate", "Fate/Zero")]
    [InlineData("Demon Slayer", "Demon Slayer: Kimetsu no Yaiba")]
    [InlineData("Frieren", "Frieren 2")]
    [InlineData("A", "A Certain Scientific Railgun")]
    public void Does_not_match_titles_that_only_share_a_prefix(string first, string second) =>
        Assert.False(MediaLibraryService.TitlesMatch(first, second));
}
