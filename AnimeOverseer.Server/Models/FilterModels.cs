namespace AnimeOverseer.Server.Models;

public class FilterState
{
    public GroupLogic TopLevelLogic { get; set; } = GroupLogic.And;
    public List<FilterGroup> Groups { get; set; } = [new FilterGroup()];

    public bool IsActive => Groups.Any(g => g.Conditions.Any(IsActiveCondition));
    public int ActiveCount => Groups.Sum(g => g.Conditions.Count(IsActiveCondition));

    public static bool IsActiveCondition(FilterCondition condition) =>
        condition.Operator is FilterOperator.ContainsData or FilterOperator.DoesNotContainData ||
        !string.IsNullOrEmpty(condition.Value);
}

public class FilterGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public GroupLogic Logic { get; set; } = GroupLogic.And;
    public List<FilterCondition> Conditions { get; set; } = [new FilterCondition()];
}

public class FilterCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FilterField Field { get; set; } = FilterField.Title;
    public FilterOperator Operator { get; set; } = FilterOperator.Contains;
    public string Value { get; set; } = string.Empty;
}

public enum GroupLogic { And, Or }

public enum FilterField
{
    Title,
    OriginalTitle,
    Type,
    Status,
    Season,
    Episodes,
    Rating,
    Genres,
    Themes,
    Demographics,
    Year,
    Synopsis,
    MalId,
    AniListId,
    KitsuId,
    Duration,
    StartDate,
    EndDate,
    CachedAt,
    InLibrary
}

public enum FilterOperator
{
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    ContainsData,
    DoesNotContainData
}
