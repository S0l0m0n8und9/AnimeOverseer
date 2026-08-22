using Microsoft.EntityFrameworkCore;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Data;

public class AnimeDbContext : DbContext
{
    public AnimeDbContext(DbContextOptions<AnimeDbContext> options)
        : base(options)
    {
    }

    public DbSet<Anime> Animes { get; set; }
    public DbSet<AnimeImage> AnimeImages { get; set; }
    public DbSet<AnimeTitleAlias> AnimeTitleAliases { get; set; }
    public DbSet<PendingAnimeReview> PendingAnimeReviews { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<AnimeGenre> AnimeGenres { get; set; }
    public DbSet<Theme> Themes { get; set; }
    public DbSet<AnimeTheme> AnimeThemes { get; set; }
    public DbSet<Demographic> Demographics { get; set; }
    public DbSet<AnimeDemographic> AnimeDemographics { get; set; }
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Request> Requests { get; set; }
    public DbSet<CachedAnimeRelation> CachedAnimeRelations { get; set; }
    public DbSet<AnimeRecommendation> AnimeRecommendations { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<SyncJob> SyncJobs { get; set; }
    public DbSet<SyncJobLog> SyncJobLogs { get; set; }
    public DbSet<IntegrationSchedule> IntegrationSchedules { get; set; }
    public DbSet<AnimeCollection> AnimeCollections { get; set; }
    public DbSet<AnimeCollectionItem> AnimeCollectionItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnimeGenre>()
            .HasKey(ag => new { ag.AnimeId, ag.GenreId });

        modelBuilder.Entity<AnimeGenre>()
            .HasOne(ag => ag.Anime)
            .WithMany(a => a.AnimeGenres)
            .HasForeignKey(ag => ag.AnimeId);

        modelBuilder.Entity<AnimeGenre>()
            .HasOne(ag => ag.Genre)
            .WithMany(g => g.AnimeGenres)
            .HasForeignKey(ag => ag.GenreId);

        modelBuilder.Entity<AnimeTheme>()
            .HasKey(at => new { at.AnimeId, at.ThemeId });

        modelBuilder.Entity<AnimeTheme>()
            .HasOne(at => at.Anime)
            .WithMany(a => a.AnimeThemes)
            .HasForeignKey(at => at.AnimeId);

        modelBuilder.Entity<AnimeTheme>()
            .HasOne(at => at.Theme)
            .WithMany(t => t.AnimeThemes)
            .HasForeignKey(at => at.ThemeId);

        modelBuilder.Entity<AnimeDemographic>()
            .HasKey(ad => new { ad.AnimeId, ad.DemographicId });

        modelBuilder.Entity<AnimeDemographic>()
            .HasOne(ad => ad.Anime)
            .WithMany(a => a.AnimeDemographics)
            .HasForeignKey(ad => ad.AnimeId);

        modelBuilder.Entity<AnimeDemographic>()
            .HasOne(ad => ad.Demographic)
            .WithMany(d => d.AnimeDemographics)
            .HasForeignKey(ad => ad.DemographicId);

        modelBuilder.Entity<AnimeImage>()
            .HasIndex(image => new { image.AnimeId, image.Source, image.Type, image.ImageUrl })
            .IsUnique();

        modelBuilder.Entity<AnimeTitleAlias>()
            .HasIndex(alias => new { alias.AnimeId, alias.Title })
            .IsUnique();

        modelBuilder.Entity<AnimeRecommendation>()
            .HasKey(recommendation => new { recommendation.SourceAnimeId, recommendation.RecommendedAnimeId });
        modelBuilder.Entity<AnimeRecommendation>()
            .HasOne<Anime>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.SourceAnimeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AnimeCollectionItem>()
            .HasKey(item => new { item.CollectionId, item.AnimeId });
        modelBuilder.Entity<AnimeCollectionItem>()
            .HasOne(item => item.Collection).WithMany(collection => collection.Items)
            .HasForeignKey(item => item.CollectionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AnimeCollectionItem>()
            .HasOne(item => item.Anime).WithMany().HasForeignKey(item => item.AnimeId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AnimeRecommendation>()
            .HasOne<Anime>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.RecommendedAnimeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PendingAnimeReview>()
            .HasIndex(review => new { review.Status, review.CreatedAt });

        modelBuilder.Entity<Anime>().HasIndex(anime => anime.StartDate);
        modelBuilder.Entity<Anime>().HasIndex(anime => anime.AniListId);
        modelBuilder.Entity<Anime>().HasIndex(anime => anime.MALId);
        modelBuilder.Entity<Season>().HasIndex(season => new { season.Year, season.Name }).IsUnique();
        modelBuilder.Entity<CachedAnimeRelation>().HasIndex(relation => relation.RootMalId);

        modelBuilder.Entity<Genre>().HasIndex(g => g.Name).IsUnique();
        modelBuilder.Entity<Theme>().HasIndex(t => t.Name).IsUnique();
        modelBuilder.Entity<Demographic>().HasIndex(d => d.Name).IsUnique();
    }
}
