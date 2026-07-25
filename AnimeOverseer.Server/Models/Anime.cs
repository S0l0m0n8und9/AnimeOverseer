using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AnimeOverseer.Server.Models;

public class AnimeRelation
{
    public string RelationType { get; set; } = string.Empty; // Prequel, Sequel, Side Story, etc.
    public int AnimeId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CachedAnimeRelation
{
    public int Id { get; set; }
    public int RootMalId { get; set; }
    public int RelatedMalId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CachedAt { get; set; }
}

public class Anime
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    // Indicates that Title came from a source's explicitly localized English field.
    // This is only used while importing and is not persisted.
    [NotMapped]
    public bool HasEnglishTitle { get; set; }

    // Provider-supplied English, romaji, native, and synonym titles used only
    // while resolving the catalogue identity. They are intentionally not stored.
    [NotMapped]
    public List<string> AlternativeTitles { get; set; } = new();

    public string? OriginalTitle { get; set; }

    public string? Synopsis { get; set; }

    public string? ImageUrl { get; set; }

    public int? MALId { get; set; }

    public int? AniListId { get; set; }

    public int? KitsuId { get; set; }

    public string? Type { get; set; } // TV, Movie, OVA, Special, ONA, Music

    public int? Episodes { get; set; }

    public int? Duration { get; set; } // in minutes

    public decimal? Rating { get; set; }

    public string? Status { get; set; } // Currently Airing, Finished, Not Yet Aired

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime? CachedAt { get; set; }

    public string? LocalImagePath { get; set; }

    // A user-selected artwork record to use in place of the catalogue's default cover.
    public int? PreferredImageId { get; set; }

    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    public List<AnimeGenre> AnimeGenres { get; set; } = new();

    public List<AnimeTheme> AnimeThemes { get; set; } = new();

    public List<AnimeDemographic> AnimeDemographics { get; set; } = new();

    public List<Request> Requests { get; set; } = new();

    public List<AnimeImage> Images { get; set; } = new();

    [NotMapped]
    public AnimeImage? PreferredImage => PreferredImageId is int imageId
        ? Images.FirstOrDefault(image => image.Id == imageId)
        : null;

    [NotMapped]
    public string? DisplayImageUrl => PreferredImage?.ImageUrl ?? ImageUrl;

    [NotMapped]
    public string? DisplayLocalImagePath => PreferredImage?.LocalImagePath ?? LocalImagePath;

    [NotMapped]
    public List<AnimeRelation> Relations { get; set; } = new();

    [NotMapped]
    public List<SourceImage> SourceImages { get; set; } = new();
}

public class AnimeImage
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public Anime Anime { get; set; } = null!;
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = "Poster"; // Poster, Banner, Background
    public string ImageUrl { get; set; } = string.Empty;
    public string? LocalImagePath { get; set; }
}

public class SourceImage
{
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = "Poster";
    public string Url { get; set; } = string.Empty;
}

public class Genre
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public List<AnimeGenre> AnimeGenres { get; set; } = new();
}

public class AnimeGenre
{
    public int AnimeId { get; set; }
    public Anime Anime { get; set; } = null!;

    public int GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
}

public class Theme
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public List<AnimeTheme> AnimeThemes { get; set; } = new();
}

public class AnimeTheme
{
    public int AnimeId { get; set; }
    public Anime Anime { get; set; } = null!;

    public int ThemeId { get; set; }
    public Theme Theme { get; set; } = null!;
}

public class Demographic
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public List<AnimeDemographic> AnimeDemographics { get; set; } = new();
}

public class AnimeDemographic
{
    public int AnimeId { get; set; }
    public Anime Anime { get; set; } = null!;

    public int DemographicId { get; set; }
    public Demographic Demographic { get; set; } = null!;
}

public class Season
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int Year { get; set; }

    public List<Anime> Animes { get; set; } = new();
}

public class Request
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Completed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public string? RequestedBy { get; set; }
}
