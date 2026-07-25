using System.Net;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public sealed class ImportIntegrationTests : SqliteIntegrationTestBase
{
    [Fact]
    public async Task Import_reuses_provider_identity_and_deduplicates_aliases_and_images()
    {
        await using var db = CreateDb();
        var cache = CreateCache(db);
        var source = Incoming(101, "Orbit", true, aliases: ["Orbit!"], images: [Image("AniList", "Poster", "https://images.test/orbit.jpg")]);
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) => Task.FromResult(new List<Anime> { source }));
        source.AlternativeTitles.Add("Orbit");
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) => Task.FromResult(new List<Anime> { source }));
        Assert.Equal(1, await db.Animes.CountAsync());
        Assert.Equal(1, await db.AnimeImages.CountAsync());
        Assert.Equal(2, await db.AnimeTitleAliases.CountAsync());
    }

    [Fact]
    public async Task Ambiguous_alias_match_creates_one_pending_review_without_a_duplicate_anime()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        db.Animes.AddRange(new Anime { AniListId = 1, Title = "First", SeasonId = season.Id, TitleAliases = [new() { Title = "Shared Name" }] }, new Anime { AniListId = 2, Title = "Second", SeasonId = season.Id, TitleAliases = [new() { Title = "Shared Name" }] });
        await db.SaveChangesAsync();
        var incoming = Incoming(999, "Shared Name", false);
        await CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["summer"], (_, _, _) => Task.FromResult(new List<Anime> { incoming }));
        await CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["summer"], (_, _, _) => Task.FromResult(new List<Anime> { incoming }));
        var review = await db.PendingAnimeReviews.SingleAsync();
        Assert.Equal("Pending", review.Status);
        Assert.Equal(2, await db.Animes.CountAsync());
        Assert.Contains("title/alias", review.Reason);
    }

    [Fact]
    public async Task Normalized_single_title_match_is_staged_for_review_with_its_only_candidate()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        var existing = new Anime { AniListId = 81, Title = "Café: The Show II", SeasonId = season.Id };
        db.Animes.Add(existing);
        await db.SaveChangesAsync();
        await CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["fall"], (_, _, _) => Task.FromResult(new List<Anime> { Incoming(82, "Cafe The Show 2", false) }));
        var review = await db.PendingAnimeReviews.SingleAsync();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new[] { existing.Id }), review.CandidateAnimeIdsJson);
        Assert.Equal(1, await db.Animes.CountAsync());
    }

    [Fact]
    public async Task Reimport_replaces_tags_without_duplicate_join_rows()
    {
        await using var db = CreateDb();
        var cache = CreateCache(db);
        var first = Incoming(111, "Tagged", true);
        first.AnimeGenres = [new AnimeGenre { Genre = new Genre { Name = "Action" } }];
        first.AnimeThemes = [new AnimeTheme { Theme = new Theme { Name = "School" } }];
        var replacement = Incoming(111, "Tagged", true);
        replacement.AnimeGenres = [new AnimeGenre { Genre = new Genre { Name = "Drama" } }];
        replacement.AnimeThemes = [new AnimeTheme { Theme = new Theme { Name = "School" } }];
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) => Task.FromResult(new List<Anime> { first }));
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) => Task.FromResult(new List<Anime> { replacement }));
        var anime = await db.Animes.Include(x => x.AnimeGenres).ThenInclude(x => x.Genre).Include(x => x.AnimeThemes).ThenInclude(x => x.Theme).SingleAsync();
        Assert.Equal(["Drama"], anime.AnimeGenres.Select(x => x.Genre.Name));
        Assert.Equal(["School"], anime.AnimeThemes.Select(x => x.Theme.Name));
    }

    [Fact]
    public async Task Failed_later_season_leaves_already_persisted_anime_intact_without_duplicates()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        db.Animes.Add(new Anime { AniListId = 77, Title = "Existing English", HasEnglishTitle = true, SeasonId = season.Id });
        await db.SaveChangesAsync();
        var calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["spring", "summer"], (_, _, _) =>
        {
            calls++;
            return calls == 1 ? Task.FromResult(new List<Anime> { Incoming(77, "Romaji Replacement", false) }) : Task.FromException<List<Anime>>(new HttpRequestException("second season failed"));
        }));
        var saved = await db.Animes.SingleAsync();
        Assert.Equal("Existing English", saved.Title);
        Assert.Equal(1, await db.Animes.CountAsync());
    }

    [Fact]
    public async Task Failed_image_download_keeps_catalogue_and_image_metadata()
    {
        await using var db = CreateDb();
        await CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) => Task.FromResult(new List<Anime> { Incoming(121, "Artwork Failure", true, images: [Image("AniList", "Poster", "https://images.test/unavailable.jpg")]) }));
        var image = await db.AnimeImages.SingleAsync();
        Assert.Equal("https://images.test/unavailable.jpg", image.ImageUrl);
        Assert.Null(image.LocalImagePath);
        Assert.Equal(1, await db.Animes.CountAsync());
    }
}
