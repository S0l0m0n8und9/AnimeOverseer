using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class SortEngine
{
    public static List<Anime> Apply(List<Anime> animes, SortState state, Func<Anime, bool>? isInLibrary = null)
    {
        if (!state.IsActive) return animes;

        IOrderedEnumerable<Anime>? ordered = null;
        foreach (var criterion in state.Criteria)
        {
            if (ordered == null)
                ordered = Order(animes, criterion, isInLibrary);
            else
                ordered = ThenOrder(ordered, criterion, isInLibrary);
        }

        return ordered?.ToList() ?? animes;
    }

    private static IOrderedEnumerable<Anime> Order(IEnumerable<Anime> source, SortCriterion c, Func<Anime, bool>? isInLibrary) =>
        c.Direction == SortDirection.Ascending
            ? source.OrderBy(a => GetKey(a, c.Field, isInLibrary))
            : source.OrderByDescending(a => GetKey(a, c.Field, isInLibrary));

    private static IOrderedEnumerable<Anime> ThenOrder(IOrderedEnumerable<Anime> source, SortCriterion c, Func<Anime, bool>? isInLibrary) =>
        c.Direction == SortDirection.Ascending
            ? source.ThenBy(a => GetKey(a, c.Field, isInLibrary))
            : source.ThenByDescending(a => GetKey(a, c.Field, isInLibrary));

    private static object? GetKey(Anime a, SortField field, Func<Anime, bool>? isInLibrary) => field switch
    {
        SortField.Title    => a.Title ?? string.Empty,
        SortField.OriginalTitle => a.OriginalTitle ?? string.Empty,
        SortField.Synopsis => a.Synopsis ?? string.Empty,
        SortField.Type     => a.Type ?? string.Empty,
        SortField.Episodes => (object?)(a.Episodes ?? 0),
        SortField.Duration => (object?)(a.Duration ?? 0),
        SortField.Rating   => (object?)(a.Rating ?? 0m),
        SortField.Status   => a.Status ?? string.Empty,
        SortField.Season   => a.Season?.Name ?? string.Empty,
        SortField.Year => a.Season?.Year ?? 0,
        SortField.Genres => string.Join(", ", a.AnimeGenres.Select(ag => ag.Genre?.Name).Where(n => n is not null)),
        SortField.Themes => string.Join(", ", a.AnimeThemes.Select(at => at.Theme?.Name).Where(n => n is not null)),
        SortField.Demographics => string.Join(", ", a.AnimeDemographics.Select(ad => ad.Demographic?.Name).Where(n => n is not null)),
        SortField.MalId => a.MALId ?? 0,
        SortField.AniListId => a.AniListId ?? 0,
        SortField.KitsuId => a.KitsuId ?? 0,
        SortField.StartDate => a.StartDate ?? DateTime.MinValue,
        SortField.EndDate => a.EndDate ?? DateTime.MinValue,
        SortField.CachedAt => a.CachedAt ?? DateTime.MinValue,
        SortField.InLibrary => isInLibrary?.Invoke(a) ?? false,
        _                  => null
    };
}
