using Microsoft.EntityFrameworkCore;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddServerSideBlazor();

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

builder.Services.AddHttpClient<JikanApiService>();
builder.Services.AddHttpClient<AniListApiService>();
builder.Services.AddHttpClient<KitsuApiService>();
builder.Services.AddSingleton<IAnimeDataSource, JikanApiService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

var hostPagePath = Path.Combine(Directory.GetCurrentDirectory(), "Pages", "_Host.cshtml");

app.UseStaticFiles();
app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapBlazorHub();
app.MapControllers();

var serveHost = async (HttpContext ctx) =>
{
    if (File.Exists(hostPagePath))
    {
        var content = await File.ReadAllTextAsync(hostPagePath);
        ctx.Response.ContentType = "text/html";
        await ctx.Response.WriteAsync(content);
    }
    else
    {
        ctx.Response.StatusCode = 404;
    }
};
app.MapGet("/", serveHost);
app.MapFallback(serveHost);

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
