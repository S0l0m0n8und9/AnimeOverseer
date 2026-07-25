using System.Net;
using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public sealed class HighRiskDataFlowTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"animeoverseer-tests-{Guid.NewGuid():N}.db");

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
        db.Animes.AddRange(new Anime { AniListId = 1, Title = "First", SeasonId = season.Id, TitleAliases = [new() { Title = "Shared Name" }] },
                           new Anime { AniListId = 2, Title = "Second", SeasonId = season.Id, TitleAliases = [new() { Title = "Shared Name" }] });
        await db.SaveChangesAsync();
        var cache = CreateCache(db);

        var incoming = Incoming(999, "Shared Name", false);
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["summer"], (_, _, _) => Task.FromResult(new List<Anime> { incoming }));
        await cache.FetchAndCacheSeasonsFromAsync(2026, ["summer"], (_, _, _) => Task.FromResult(new List<Anime> { incoming }));

        var review = await db.PendingAnimeReviews.SingleAsync();
        Assert.Equal("Pending", review.Status);
        Assert.Equal(2, await db.Animes.CountAsync());
        Assert.Contains("title/alias", review.Reason);
    }

    [Fact]
    public async Task Merge_review_keeps_english_primary_title_and_collects_incoming_aliases_and_images()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        var existing = new Anime { AniListId = 10, Title = "English Primary", HasEnglishTitle = true, SeasonId = season.Id, TitleAliases = [new() { Title = "Old Alias" }] };
        db.Animes.Add(existing);
        await db.SaveChangesAsync();
        var review = AddReview(season.Id, Incoming(20, "Romaji Name", false, aliases: ["Japanese Name"], images: [Image("MAL", "Poster", "https://images.test/merge.jpg")]), [existing.Id]);
        db.PendingAnimeReviews.Add(review);
        await db.SaveChangesAsync();

        Assert.True(await CreateCache(db).ResolvePendingReviewAsync(review.Id, "Merge", existing.Id));

        var merged = await db.Animes.Include(x => x.TitleAliases).Include(x => x.Images).SingleAsync();
        Assert.Equal("English Primary", merged.Title);
        // A merge must not replace an established provider identity with a conflicting one.
        Assert.Equal(10, merged.AniListId);
        Assert.Contains(merged.TitleAliases, x => x.Title == "Romaji Name");
        Assert.Contains(merged.TitleAliases, x => x.Title == "Japanese Name");
        Assert.Single(merged.Images);
        Assert.Equal("Merged", (await db.PendingAnimeReviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task Create_review_persists_a_new_anime_with_its_aliases_and_images()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        var review = AddReview(season.Id, Incoming(30, "New English", true, aliases: ["New Native"], images: [Image("AniList", "Banner", "https://images.test/new.jpg")]), []);
        db.PendingAnimeReviews.Add(review);
        await db.SaveChangesAsync();

        Assert.True(await CreateCache(db).ResolvePendingReviewAsync(review.Id, "Create"));

        var created = await db.Animes.Include(x => x.TitleAliases).Include(x => x.Images).SingleAsync();
        Assert.Equal("New English", created.Title);
        Assert.Equal(30, created.AniListId);
        Assert.Contains(created.TitleAliases, x => x.Title == "New Native");
        Assert.Single(created.Images);
        Assert.Equal("Created", (await db.PendingAnimeReviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancelling_queued_and_running_jobs_preserves_the_correct_job_state()
    {
        await using var db = CreateDb();
        db.SyncJobs.AddRange(new SyncJob { JobType = "AniList" }, new SyncJob { JobType = "AniList", Status = "Running" });
        await db.SaveChangesAsync();
        var jobs = new SyncJobService(db, new SyncJobTrigger());

        Assert.True(await jobs.CancelAsync(1));
        Assert.True(await jobs.CancelAsync(2));

        var saved = await db.SyncJobs.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal("Cancelled", saved[0].Status);
        Assert.False(saved[0].CancellationRequested);
        Assert.True(saved[1].CancellationRequested);
        Assert.Equal("Running", saved[1].Status);
    }

    [Fact]
    public async Task Failed_later_season_leaves_already_persisted_anime_intact_without_duplicates()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        db.Animes.Add(new Anime { AniListId = 77, Title = "Existing English", HasEnglishTitle = true, SeasonId = season.Id });
        await db.SaveChangesAsync();
        var cache = CreateCache(db);
        var calls = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.FetchAndCacheSeasonsFromAsync(2026, ["spring", "summer"], (_, _, _) =>
        {
            calls++;
            return calls == 1
                ? Task.FromResult(new List<Anime> { Incoming(77, "Romaji Replacement", false) })
                : Task.FromException<List<Anime>>(new HttpRequestException("second season failed"));
        }));

        var saved = await db.Animes.SingleAsync();
        Assert.Equal("Existing English", saved.Title);
        Assert.Equal(77, saved.AniListId);
        Assert.Equal(1, await db.Animes.CountAsync());
    }

    private AnimeDbContext CreateDb()
    {
        var db = new AnimeDbContext(new DbContextOptionsBuilder<AnimeDbContext>().UseSqlite($"Data Source={_databasePath};Pooling=False").Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Season> AddSeasonAsync(AnimeDbContext db)
    {
        var season = new Season { Name = "spring", Year = 2026 };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        return season;
    }

    private static AnimeCacheService CreateCache(AnimeDbContext db)
    {
        var client = new HttpClient(new FailingHandler());
        var aniList = new AniListApiService(client, NullLogger<AniListApiService>.Instance);
        var imageCache = new ImageCacheService(new TestHttpClientFactory(client), new TestWebHostEnvironment());
        return new AnimeCacheService(db, aniList, imageCache);
    }

    private static Anime Incoming(int aniListId, string title, bool english, List<string>? aliases = null, List<SourceImage>? images = null)
        => new() { AniListId = aniListId, Title = title, HasEnglishTitle = english, AlternativeTitles = aliases ?? [], SourceImages = images ?? [] };

    private static SourceImage Image(string source, string type, string url) => new() { Source = source, Type = type, Url = url };

    private static PendingAnimeReview AddReview(int seasonId, Anime incoming, int[] candidates) => new()
    {
        SeasonId = seasonId, SourceAniListId = incoming.AniListId, SourceName = "test",
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new AnimeImportSnapshot
        {
            AniListId = incoming.AniListId, Title = incoming.Title, HasEnglishTitle = incoming.HasEnglishTitle,
            AlternativeTitles = incoming.AlternativeTitles, SourceImages = incoming.SourceImages
        }),
        CandidateAnimeIdsJson = System.Text.Json.JsonSerializer.Serialize(candidates)
    };

    public void Dispose()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), $"animeoverseer-images-{Guid.NewGuid():N}");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "AnimeOverseer.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
