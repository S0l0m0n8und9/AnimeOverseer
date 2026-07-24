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

builder.Services.AddHttpClient<AniListApiService>();
builder.Services.AddHttpClient<MyAnimeListApiService>();
builder.Services.AddHttpClient<AnimeScheduleApiService>();
// Retained only so legacy Kitsu enrichment jobs can finish; it is not a catalogue source.
builder.Services.AddHttpClient<KitsuApiService>();
builder.Services.AddScoped<AnimeCacheService>();
builder.Services.AddScoped<IAnimeDataSource>(sp => sp.GetRequiredService<AnimeCacheService>());
builder.Services.AddSingleton<ImageCacheService>();
builder.Services.AddScoped<StorageUsageService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<MediaRequestService>();
builder.Services.AddSingleton<MediaLibraryService>();
builder.Services.AddSingleton<SyncJobTrigger>();
builder.Services.AddScoped<SyncJobService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddHostedService<DailyAiringRefreshService>();
builder.Services.AddHostedService<SyncJobRunnerService>();
builder.Services.AddHostedService<NightlySyncService>();

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
    await db.Database.MigrateAsync();

    if (!db.Genres.Any())
    {
        var genres = new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Horror", "Mystery", "Romance", "Sci-Fi", "Slice of Life", "Sports", "Supernatural", "Thriller" };
        foreach (var name in genres)
            db.Genres.Add(new Genre { Name = name });
        await db.SaveChangesAsync();
    }

    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    Directory.CreateDirectory(Path.Combine(env.WebRootPath, "images", "cache"));
}

app.Run();
