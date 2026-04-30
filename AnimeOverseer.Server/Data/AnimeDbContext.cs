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
    public DbSet<Genre> Genres { get; set; }
    public DbSet<AnimeGenre> AnimeGenres { get; set; }
    public DbSet<Theme> Themes { get; set; }
    public DbSet<AnimeTheme> AnimeThemes { get; set; }
    public DbSet<Demographic> Demographics { get; set; }
    public DbSet<AnimeDemographic> AnimeDemographics { get; set; }
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Request> Requests { get; set; }
    public DbSet<CachedAnimeRelation> CachedAnimeRelations { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<SyncJob> SyncJobs { get; set; }
    public DbSet<SyncJobLog> SyncJobLogs { get; set; }

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
    }
}
