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

/// <summary>Persisted catalogue source automation. One record represents one source's cadence.</summary>
public class IntegrationSchedule
{
    public int Id { get; set; }
    public string Name { get; set; } = "New schedule";
    public string Source { get; set; } = string.Empty;
    /// <summary>Extensible scheduled action: CatalogueSync, InitialMigration, or Recommendations.</summary>
    public string WorkType { get; set; } = "CatalogueSync";
    public bool Enabled { get; set; }
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public string RecurrenceType { get; set; } = "Daily";
    public int Interval { get; set; } = 1;
    public string WeekdaysJson { get; set; } = "[]";
    public string MonthlyMode { get; set; } = "DayOfMonth";
    public int DayOfMonth { get; set; } = 1;
    public int NthWeek { get; set; } = 1;
    public int Weekday { get; set; }
    public string EndType { get; set; } = "Never";
    public int EndAfterOccurrences { get; set; } = 10;
    public DateTime? EndBy { get; set; }
    public int QueuedOccurrences { get; set; }
    // Catalogue scope is relative to when the job runs, not when it is configured.
    public int YearOffset { get; set; }
    public int YearCount { get; set; } = 1;
    public string SeasonsJson { get; set; } = "[\"winter\",\"spring\",\"summer\",\"fall\"]";
    public DateTime? LastQueuedAt { get; set; }
}
