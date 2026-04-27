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
    }
}
