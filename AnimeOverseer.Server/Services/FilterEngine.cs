using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class FilterEngine
{
    public static List<Anime> Apply(List<Anime> animes, FilterState state, Func<Anime, bool>? isInLibrary = null)
    {
        var activeGroups = state.Groups
            .Where(FilterState.IsActiveGroup)
            .ToList();

        if (activeGroups.Count == 0) return animes;

        return state.TopLevelLogic == GroupLogic.And
            ? animes.Where(a => activeGroups.All(g => MatchGroup(a, g, isInLibrary))).ToList()
            : animes.Where(a => activeGroups.Any(g => MatchGroup(a, g, isInLibrary))).ToList();
    }

    private static bool MatchGroup(Anime anime, FilterGroup group, Func<Anime, bool>? isInLibrary)
    {
        var conditions = group.Conditions.Where(FilterState.IsActiveCondition)
            .Select(c => MatchCondition(anime, c, isInLibrary));
        var subgroups = group.Subgroups.Where(FilterState.IsActiveGroup)
            .Select(g => MatchGroup(anime, g, isInLibrary));
        var matches = conditions.Concat(subgroups).ToList();
        if (matches.Count == 0) return true;

        return group.Logic == GroupLogic.And
            ? matches.All(match => match)
            : matches.Any(match => match);
    }

    private static bool MatchCondition(Anime anime, FilterCondition c, Func<Anime, bool>? isInLibrary)
    {
        if (!FilterState.IsActiveCondition(c)) return true;

        if (c.Operator is FilterOperator.ContainsData or FilterOperator.DoesNotContainData)
            return MatchConditionValue(anime, c, c.Value.Trim(), isInLibrary);

        var values = c.EffectiveValues.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        return c.ValueLogic == ValueLogic.All
            ? values.All(value => MatchConditionValue(anime, c, value.Trim(), isInLibrary))
            : values.Any(value => MatchConditionValue(anime, c, value.Trim(), isInLibrary));
    }

    private static bool MatchConditionValue(Anime anime, FilterCondition c, string val, Func<Anime, bool>? isInLibrary)
    {

        return c.Field switch
        {
            FilterField.Title        => MatchText(anime.Title, c.Operator, val),
            FilterField.OriginalTitle => MatchText(anime.OriginalTitle, c.Operator, val),
            FilterField.Synopsis     => MatchText(anime.Synopsis, c.Operator, val),
            FilterField.Type         => MatchText(anime.Type, c.Operator, val),
            FilterField.Status       => MatchText(anime.Status, c.Operator, val),
            FilterField.Season       => MatchText(anime.Season?.Name, c.Operator, val),
            FilterField.Episodes     => MatchNumber((double?)anime.Episodes, c.Operator, val),
            FilterField.Duration     => MatchNumber((double?)anime.Duration, c.Operator, val),
            FilterField.Rating       => MatchNumber((double?)anime.Rating, c.Operator, val),
            FilterField.Genres       => MatchTags(anime.AnimeGenres.Select(ag => ag.Genre?.Name ?? ""), c.Operator, val),
            FilterField.Themes       => MatchTags(anime.AnimeThemes.Select(at => at.Theme?.Name ?? ""), c.Operator, val),
            FilterField.Demographics => MatchTags(anime.AnimeDemographics.Select(ad => ad.Demographic?.Name ?? ""), c.Operator, val),
            FilterField.Year         => MatchNumber((double?)anime.Season?.Year, c.Operator, val),
            FilterField.MalId        => MatchNumber((double?)anime.MALId, c.Operator, val),
            FilterField.AniListId    => MatchNumber((double?)anime.AniListId, c.Operator, val),
            FilterField.KitsuId      => MatchNumber((double?)anime.KitsuId, c.Operator, val),
            FilterField.StartDate    => MatchDate(anime.StartDate, c.Operator, val),
            FilterField.EndDate      => MatchDate(anime.EndDate, c.Operator, val),
            FilterField.CachedAt     => MatchDate(anime.CachedAt, c.Operator, val),
            FilterField.CreatedAt    => MatchDate(anime.CreatedAt, c.Operator, val),
            FilterField.ModifiedAt   => MatchDate(anime.ModifiedAt, c.Operator, val),
            FilterField.InLibrary    => MatchBoolean(isInLibrary?.Invoke(anime) ?? false, c.Operator, val),
            _                        => true
        };
    }

    private static bool MatchTags(IEnumerable<string> names, FilterOperator op, string val)
    {
        var list = names.ToList();
        return op switch
        {
            FilterOperator.ContainsData => list.Count > 0,
            FilterOperator.DoesNotContainData => list.Count == 0,
            FilterOperator.NotContains => list.All(n => !n.Contains(val, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.NotEquals   => list.All(n => !n.Equals(val, StringComparison.OrdinalIgnoreCase)),
            _                          => list.Any(n => MatchText(n, op, val))
        };
    }

    private static bool MatchText(string? field, FilterOperator op, string val)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (op == FilterOperator.ContainsData) return !string.IsNullOrEmpty(field);
        if (op == FilterOperator.DoesNotContainData) return string.IsNullOrEmpty(field);

        field ??= string.Empty;
        return op switch
        {
            FilterOperator.Contains           => field.Contains(val, cmp),
            FilterOperator.NotContains        => !field.Contains(val, cmp),
            FilterOperator.StartsWith         => field.StartsWith(val, cmp),
            FilterOperator.EndsWith           => field.EndsWith(val, cmp),
            FilterOperator.Equals             => field.Equals(val, cmp),
            FilterOperator.NotEquals          => !field.Equals(val, cmp),
            _                                 => true
        };
    }

    private static bool MatchNumber(double? field, FilterOperator op, string val)
    {
        if (op == FilterOperator.ContainsData) return field.HasValue;
        if (op == FilterOperator.DoesNotContainData) return !field.HasValue;
        if (!double.TryParse(val, out var target)) return true;
        if (!field.HasValue) return op == FilterOperator.NotEquals;

        return op switch
        {
            FilterOperator.Equals             => field.Value == target,
            FilterOperator.NotEquals          => field.Value != target,
            FilterOperator.GreaterThan        => field.Value > target,
            FilterOperator.LessThan           => field.Value < target,
            FilterOperator.GreaterThanOrEqual => field.Value >= target,
            FilterOperator.LessThanOrEqual    => field.Value <= target,
            _                                 => true
        };
    }

    private static bool MatchDate(DateTime? field, FilterOperator op, string val)
    {
        if (op == FilterOperator.ContainsData) return field.HasValue;
        if (op == FilterOperator.DoesNotContainData) return !field.HasValue;
        if (!DateTime.TryParse(val, out var target)) return true;
        if (!field.HasValue) return op == FilterOperator.NotEquals;

        return op switch
        {
            FilterOperator.Equals             => field.Value.Date == target.Date,
            FilterOperator.NotEquals          => field.Value.Date != target.Date,
            FilterOperator.GreaterThan        => field.Value.Date > target.Date,
            FilterOperator.LessThan           => field.Value.Date < target.Date,
            FilterOperator.GreaterThanOrEqual => field.Value.Date >= target.Date,
            FilterOperator.LessThanOrEqual    => field.Value.Date <= target.Date,
            _                                 => true
        };
    }

    private static bool MatchDate(DateTime field, FilterOperator op, string val) =>
        MatchDate((DateTime?)field, op, val);

    private static bool MatchBoolean(bool field, FilterOperator op, string val)
    {
        if (!bool.TryParse(val, out var target))
            target = val.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            FilterOperator.Equals => field == target,
            FilterOperator.NotEquals => field != target,
            _ => true
        };
    }
}
