namespace AnimeOverseer.Server.Models;

public class SortState
{
    public List<SortCriterion> Criteria { get; set; } = [];
    public bool IsActive => Criteria.Count > 0;
}

public class SortCriterion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SortField Field { get; set; } = SortField.Title;
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}

public enum SortField
{
    Title,
    Type,
    Episodes,
    Rating,
    Status,
    Season,
    OriginalTitle,
    Synopsis,
    Genres,
    Themes,
    Demographics,
    Year,
    MalId,
    AniListId,
    KitsuId,
    Duration,
    StartDate,
    EndDate,
    CachedAt,
    InLibrary
}

public enum SortDirection
{
    Ascending,
    Descending
}
