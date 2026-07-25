using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using AnimeOverseer.Server.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public sealed class SyncJobIntegrationTests : SqliteIntegrationTestBase
{
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
}
