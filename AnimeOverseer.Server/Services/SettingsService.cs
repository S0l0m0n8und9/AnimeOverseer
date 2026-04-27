using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class SettingsService(AnimeDbContext db)
{
    public async Task<string?> GetAsync(string key)
        => (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetAsync(string key, string? value)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing != null)
            existing.Value = value;
        else
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        await db.SaveChangesAsync();
    }
}
