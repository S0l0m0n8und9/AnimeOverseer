using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class FilterEngine
{
    public static List<Anime> Apply(List<Anime> animes, FilterState state)
    {
        var activeGroups = state.Groups
            .Where(g => g.Conditions.Any(c => !string.IsNullOrEmpty(c.Value)))
            .ToList();

        if (activeGroups.Count == 0) return animes;

        return state.TopLevelLogic == GroupLogic.And
            ? animes.Where(a => activeGroups.All(g => MatchGroup(a, g))).ToList()
            : animes.Where(a => activeGroups.Any(g => MatchGroup(a, g))).ToList();
    }

    private static bool MatchGroup(Anime anime, FilterGroup group)
    {
        var conditions = group.Conditions.Where(c => !string.IsNullOrEmpty(c.Value)).ToList();
        if (conditions.Count == 0) return true;

        return group.Logic == GroupLogic.And
            ? conditions.All(c => MatchCondition(anime, c))
            : conditions.Any(c => MatchCondition(anime, c));
    }

    private static bool MatchCondition(Anime anime, FilterCondition c)
    {
        var val = c.Value.Trim();
        if (string.IsNullOrEmpty(val)) return true;

        return c.Field switch
        {
            FilterField.Title        => MatchText(anime.Title ?? "", c.Operator, val),
            FilterField.OriginalTitle => MatchText(anime.OriginalTitle ?? "", c.Operator, val),
            FilterField.Type         => MatchText(anime.Type ?? "", c.Operator, val),
            FilterField.Status       => MatchText(anime.Status ?? "", c.Operator, val),
            FilterField.Season       => MatchText(anime.Season?.Name ?? "", c.Operator, val),
            FilterField.Episodes     => MatchNumber((double?)anime.Episodes, c.Operator, val),
            FilterField.Rating       => MatchNumber((double?)anime.Rating, c.Operator, val),
            FilterField.Genres       => MatchTags(anime.AnimeGenres.Select(ag => ag.Genre?.Name ?? ""), c.Operator, val),
            FilterField.Themes       => MatchTags(anime.AnimeThemes.Select(at => at.Theme?.Name ?? ""), c.Operator, val),
            FilterField.Demographics => MatchTags(anime.AnimeDemographics.Select(ad => ad.Demographic?.Name ?? ""), c.Operator, val),
            FilterField.Year         => MatchNumber((double?)anime.Season?.Year, c.Operator, val),
            _                        => true
        };
    }

    private static bool MatchTags(IEnumerable<string> names, FilterOperator op, string val)
    {
        var list = names.ToList();
        return op switch
        {
            FilterOperator.NotContains => list.All(n => !n.Contains(val, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.NotEquals   => list.All(n => !n.Equals(val, StringComparison.OrdinalIgnoreCase)),
            _                          => list.Any(n => MatchText(n, op, val))
        };
    }

    private static bool MatchText(string field, FilterOperator op, string val)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
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
}
