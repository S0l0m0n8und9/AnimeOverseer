using Microsoft.EntityFrameworkCore;
using AnimeOverseer.Server;
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

builder.Services.AddHttpClient<IAnimeDataSource, JikanApiService>();
builder.Services.AddHttpClient<AniListApiService>();
builder.Services.AddHttpClient<KitsuApiService>();

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
    if (!db.Genres.Any())
    {
        var genres = new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Horror", "Mystery", "Romance", "Sci-Fi", "Slice of Life", "Sports", "Supernatural", "Thriller" };
        foreach (var name in genres)
        {
            db.Genres.Add(new Genre { Name = name });
        }
        await db.SaveChangesAsync();
    }
}

app.Run();
