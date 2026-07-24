namespace AnimeOverseer.Server.Models;

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class SyncJobLog
{
    public int Id { get; set; }
    public int SyncJobId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Info";    // Info, Success, Warning, Error
    public string Message { get; set; } = string.Empty;
}

public class SyncJob
{
    public int Id { get; set; }
    public string JobType { get; set; } = string.Empty; // "Jikan", "AniList", "Kitsu"
    public string Status { get; set; } = "Queued";     // Queued, Running, Completed, Failed
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public bool CancellationRequested { get; set; }
    public string? Message { get; set; }
    public string? Parameters { get; set; }
    // JSON array of this application's Anime.Id values successfully handled by this job.
    public string? SyncedAnimeIds { get; set; }
}
