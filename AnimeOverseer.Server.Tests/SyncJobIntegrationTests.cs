using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using AnimeOverseer.Server.Tests.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public sealed class SyncJobIntegrationTests : SqliteIntegrationTestBase
{
    [Fact]
    public async Task Recommendation_schedule_queues_a_dedicated_recommendation_job()
    {
        await using var db = CreateDb();
        var schedules = new IntegrationScheduleService(db, new SyncJobTrigger());
        var start = DateTime.UtcNow.AddMinutes(-2);

        await schedules.SaveAsync(new IntegrationSchedule
        {
            Name = "Refresh recommendations",
            Source = "MyAnimeList", // Recommendation retrieval is always AniList-backed.
            WorkType = "Recommendations",
            Enabled = true,
            StartAt = start,
            RecurrenceType = "Once"
        });

        await schedules.QueueDueAsync();

        var schedule = await db.IntegrationSchedules.SingleAsync();
        var job = await db.SyncJobs.SingleAsync();
        Assert.Equal("AniList", schedule.Source);
        Assert.Equal("Recommendations", job.JobType);
        Assert.Contains("years", job.Parameters);
        Assert.Contains("seasons", job.Parameters);
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
    public async Task Cancellation_before_a_season_prevents_fetching_or_persisting_it()
    {
        await using var db = CreateDb();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateCache(db).FetchAndCacheSeasonsFromAsync(2026, ["spring"], (_, _, _) =>
        {
            calls++;
            return Task.FromResult(new List<Anime> { Incoming(131, "Should Not Import", true) });
        }, ct: cancellation.Token));
        Assert.Equal(0, calls);
        Assert.Equal(0, await db.Animes.CountAsync());
    }

    [Fact]
    public async Task Runner_recovers_stale_running_jobs_on_startup()
    {
        await using (var db = CreateDb())
        {
            db.SyncJobs.AddRange(
                new SyncJob { JobType = "AniList", Status = "Running" },
                new SyncJob { JobType = "AniList", Status = "Running", CancellationRequested = true });
            await db.SaveChangesAsync();
        }

        await using var provider = CreateRunnerProvider();
        var runner = provider.GetRequiredService<SyncJobRunnerService>();
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var check = CreateDb();
            return await check.SyncJobs.AllAsync(job => job.FinishedAt != null);
        });
        await runner.StopAsync(CancellationToken.None);

        await using var saved = CreateDb();
        var jobs = await saved.SyncJobs.OrderBy(job => job.Id).ToListAsync();
        Assert.Equal("Failed", jobs[0].Status);
        Assert.Equal("Cancelled", jobs[1].Status);
    }

    [Fact]
    public async Task Runner_observes_cancellation_request_and_finishes_job_as_cancelled()
    {
        await using (var db = CreateDb())
        {
            db.SyncJobs.Add(new SyncJob { JobType = "AniList", Parameters = "{\"years\":[2026],\"seasons\":[\"spring\"]}" });
            await db.SaveChangesAsync();
        }

        var blockingHandler = new BlockingHandler();
        await using var provider = CreateRunnerProvider(blockingHandler);
        var runner = provider.GetRequiredService<SyncJobRunnerService>();
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var check = CreateDb();
            return await check.SyncJobs.SingleAsync().ContinueWith(task => task.Result.Status == "Running");
        });

        using (var scope = provider.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<SyncJobService>().CancelAsync(1));

        await WaitUntilAsync(async () =>
        {
            await using var check = CreateDb();
            return (await check.SyncJobs.SingleAsync()).Status == "Cancelled";
        }, TimeSpan.FromSeconds(5));
        await runner.StopAsync(CancellationToken.None);

        await using var saved = CreateDb();
        var job = await saved.SyncJobs.SingleAsync();
        Assert.Equal("Cancelled", job.Status);
        Assert.Equal("Cancelled", job.Message);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task Kitsu_item_failure_is_logged_and_does_not_stop_remaining_items()
    {
        await using var db = CreateDb();
        var season = await AddSeasonAsync(db);
        db.Animes.AddRange(
            new Anime { MALId = 401, Title = "Transient failure", SeasonId = season.Id },
            new Anime { MALId = 402, Title = "Continues syncing", SeasonId = season.Id });
        var job = new SyncJob { JobType = "Kitsu" };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        using var services = new ServiceCollection().BuildServiceProvider();
        var handler = new SequenceHandler();
        var sync = new SyncService(
            db,
            services.GetRequiredService<IServiceScopeFactory>(),
            new AniListApiService(new HttpClient(), NullLogger<AniListApiService>.Instance),
            new MyAnimeListApiService(new HttpClient(), new ConfigurationBuilder().Build(), new SettingsService(db)),
            new AnimeScheduleApiService(new HttpClient(), new SettingsService(db)),
            new KitsuApiService(new HttpClient(handler)),
            NullLogger<SyncService>.Instance);

        await sync.RunKitsuSyncAsync(job, CancellationToken.None);

        Assert.Equal(2, job.ProcessedCount);
        var logs = await db.SyncJobLogs.Where(log => log.SyncJobId == job.Id).Select(log => log.Message).ToListAsync();
        Assert.Contains(logs, message => message.Contains("Kitsu item MAL 401 (Transient failure) failed (HttpRequestException)"));
        Assert.Contains(logs, message => message.Contains("Sync complete — 2 processed, 1 errors"));
    }

    [Fact]
    public async Task Runner_records_a_fatal_sync_failure()
    {
        await using (var db = CreateDb())
        {
            db.SyncJobs.Add(new SyncJob { JobType = "AniList", Parameters = "{\"years\":[2026],\"seasons\":[\"spring\"]}" });
            await db.SaveChangesAsync();
        }

        await using var provider = CreateRunnerProvider(new ThrowingHandler());
        var runner = provider.GetRequiredService<SyncJobRunnerService>();
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var check = CreateDb();
            return (await check.SyncJobs.SingleAsync()).Status == "Failed";
        });
        await runner.StopAsync(CancellationToken.None);

        await using var saved = CreateDb();
        var job = await saved.SyncJobs.SingleAsync();
        Assert.Equal("Failed", job.Status);
        Assert.False(string.IsNullOrWhiteSpace(job.Message));
        Assert.Contains(await saved.SyncJobLogs.Select(log => log.Message).ToListAsync(), message => message.StartsWith("Sync failed —"));
    }

    private ServiceProvider CreateRunnerProvider(HttpMessageHandler? cacheHandler = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AnimeOverseer.Server.Data.AnimeDbContext(
            new DbContextOptionsBuilder<AnimeOverseer.Server.Data.AnimeDbContext>().UseSqlite(ConnectionString).Options));
        services.AddSingleton<SyncJobTrigger>();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<HttpClient>();
        services.AddScoped<SettingsService>();
        services.AddScoped<AniListApiService>(service => new AniListApiService(cacheHandler is null ? new HttpClient() : new HttpClient(cacheHandler), NullLogger<AniListApiService>.Instance));
        services.AddScoped<MyAnimeListApiService>();
        services.AddScoped<AnimeScheduleApiService>();
        services.AddScoped<KitsuApiService>(service => new KitsuApiService(new HttpClient()));
        services.AddScoped<AnimeCacheService>(service => CreateCache(service.GetRequiredService<AnimeOverseer.Server.Data.AnimeDbContext>(), cacheHandler));
        services.AddScoped<SyncService>();
        services.AddScoped<SyncJobService>();
        services.AddSingleton<SyncJobRunnerService>();
        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("Timed out waiting for background job state.");
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request should have been cancelled.");
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("temporary provider failure"));
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("temporary provider failure"))
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}")
                });
    }
}
