using Microsoft.EntityFrameworkCore;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddServerSideBlazor();
builder.Services.AddRazorPages();
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<AnimeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? 
        "Data Source=animeoverseer.db"));

// CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Anime data sources - register all three, default to Jikan
builder.Services.AddHttpClient<JikanApiService>();
builder.Services.AddHttpClient<AniListApiService>();
builder.Services.AddHttpClient<KitsuApiService>();

// Register the primary data source (Jikan as default)
builder.Services.AddSingleton<IAnimeDataSource, JikanApiService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.UseSwagger();
//     app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapControllers();

// Seed initial data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
    db.Database.EnsureCreated();
    
    // Seed genres if not present
    if (!db.Genres.Any())
    {
        var genres = new[]
        {
            "Action", "Adventure", "Comedy", "Drama", "Fantasy",
            "Horror", "Mystery", "Romance", "Sci-Fi", "Slice of Life",
            "Sports", "Supernatural", "Thriller"
        };
        foreach (var name in genres)
        {
            db.Genres.Add(new Genre { Name = name });
        }
        await db.SaveChangesAsync();
    }
}

app.Run();