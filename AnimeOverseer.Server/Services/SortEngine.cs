using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class SortEngine
{
    public static List<Anime> Apply(List<Anime> animes, SortState state)
    {
        if (!state.IsActive) return animes;

        IOrderedEnumerable<Anime>? ordered = null;
        foreach (var criterion in state.Criteria)
        {
            if (ordered == null)
                ordered = Order(animes, criterion);
            else
                ordered = ThenOrder(ordered, criterion);
        }

        return ordered?.ToList() ?? animes;
    }

    private static IOrderedEnumerable<Anime> Order(IEnumerable<Anime> source, SortCriterion c) =>
        c.Direction == SortDirection.Ascending
            ? source.OrderBy(a => GetKey(a, c.Field))
            : source.OrderByDescending(a => GetKey(a, c.Field));

    private static IOrderedEnumerable<Anime> ThenOrder(IOrderedEnumerable<Anime> source, SortCriterion c) =>
        c.Direction == SortDirection.Ascending
            ? source.ThenBy(a => GetKey(a, c.Field))
            : source.ThenByDescending(a => GetKey(a, c.Field));

    private static object? GetKey(Anime a, SortField field) => field switch
    {
        SortField.Title    => a.Title ?? string.Empty,
        SortField.Type     => a.Type ?? string.Empty,
        SortField.Episodes => (object?)(a.Episodes ?? 0),
        SortField.Rating   => (object?)(a.Rating ?? 0m),
        SortField.Status   => a.Status ?? string.Empty,
        SortField.Season   => a.Season?.Name ?? string.Empty,
        _                  => null
    };
}
