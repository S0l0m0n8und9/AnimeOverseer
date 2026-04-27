using Microsoft.EntityFrameworkCore;
using AnimeOverseer.Server;
using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AnimeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ??
        "Data Source=animeoverseer.db"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// JikanApiService gets its own typed HttpClient; AnimeCacheService is the primary IAnimeDataSource
builder.Services.AddHttpClient<JikanApiService>();
builder.Services.AddHttpClient<AniListApiService>();
builder.Services.AddHttpClient<KitsuApiService>();
builder.Services.AddScoped<IAnimeDataSource, AnimeCacheService>();
builder.Services.AddSingleton<ImageCacheService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SyncJobService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddHostedService<AnimeRefreshBackgroundService>();
builder.Services.AddHostedService<SyncJobRunnerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseCors("AllowReactApp");
app.UseAntiforgery();
app.UseAuthorization();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
    db.Database.EnsureCreated();

    // Add new columns only if they don't already exist
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info(Animes)";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            existingColumns.Add(reader.GetString(1));
    }
    if (!existingColumns.Contains("CachedAt"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Animes ADD COLUMN CachedAt TEXT");
    if (!existingColumns.Contains("LocalImagePath"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Animes ADD COLUMN LocalImagePath TEXT");

    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='CachedAnimeRelations'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists == null)
        {
            db.Database.ExecuteSqlRaw(@"CREATE TABLE CachedAnimeRelations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RootMalId INTEGER NOT NULL,
                RelatedMalId INTEGER NOT NULL,
                RelationType TEXT NOT NULL,
                Name TEXT NOT NULL,
                CachedAt TEXT NOT NULL
            )");
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_CachedAnimeRelations_RootMalId ON CachedAnimeRelations (RootMalId)");
        }
    }

    if (!db.Genres.Any())
    {
        var genres = new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Horror", "Mystery", "Romance", "Sci-Fi", "Slice of Life", "Sports", "Supernatural", "Thriller" };
        foreach (var name in genres)
            db.Genres.Add(new Genre { Name = name });
        await db.SaveChangesAsync();
    }

    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='AppSettings'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            db.Database.ExecuteSqlRaw(@"CREATE TABLE AppSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT
            )");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_AppSettings_Key ON AppSettings (Key)");
        }
    }

    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SyncJobs'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            db.Database.ExecuteSqlRaw(@"CREATE TABLE SyncJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobType TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Queued',
                QueuedAt TEXT NOT NULL,
                StartedAt TEXT,
                FinishedAt TEXT,
                ProcessedCount INTEGER NOT NULL DEFAULT 0,
                TotalCount INTEGER NOT NULL DEFAULT 0,
                Message TEXT
            )");
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_SyncJobs_Status ON SyncJobs (Status)");
        }
    }

    // Ensure image cache directory exists
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    Directory.CreateDirectory(Path.Combine(env.WebRootPath, "images", "cache"));
}

app.Run();
