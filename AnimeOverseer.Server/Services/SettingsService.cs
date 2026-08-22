using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnimeOverseer.Server.Services;

public class SettingsService(AnimeDbContext db, IMemoryCache memoryCache)
{
    public async Task<string?> GetAsync(string key)
    {
        var cacheKey = $"settings:{key}";
        if (memoryCache.TryGetValue(cacheKey, out string? cached)) return cached;

        var value = (await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;
        memoryCache.Set(cacheKey, value, TimeSpan.FromMinutes(10));
        return value;
    }

    public async Task SetAsync(string key, string? value)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing != null)
            existing.Value = value;
        else
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        await db.SaveChangesAsync();
        memoryCache.Remove($"settings:{key}");
    }
}
