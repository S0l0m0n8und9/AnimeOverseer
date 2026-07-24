namespace AnimeOverseer.Server.Services;

public class ImageCacheService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _cacheDirectory;

    public ImageCacheService(IHttpClientFactory httpClientFactory, IWebHostEnvironment env)
    {
        _httpClientFactory = httpClientFactory;
        _cacheDirectory = Path.Combine(env.WebRootPath, "images", "cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> CacheImageAsync(string? imageUrl, int malId)
    {
        if (string.IsNullOrEmpty(imageUrl)) return null;

        var fileName = $"mal_{malId}.jpg";
        var filePath = Path.Combine(_cacheDirectory, fileName);
        var webPath = $"/images/cache/{fileName}";

        if (File.Exists(filePath))
            return webPath;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var bytes = await client.GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(filePath, bytes);
            return webPath;
        }
        catch
        {
            return File.Exists(filePath) ? webPath : null;
        }
    }

    public async Task<string?> CacheArtworkAsync(string imageUrl, int animeId, string source, string type)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(imageUrl)))[..12].ToLowerInvariant();
        var safeSource = string.Concat(source.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var safeType = string.Concat(type.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var fileName = $"anime_{animeId}_{safeSource}_{safeType}_{hash}.jpg";
        var filePath = Path.Combine(_cacheDirectory, fileName);
        var webPath = $"/images/cache/{fileName}";
        if (File.Exists(filePath)) return webPath;
        try
        {
            var bytes = await _httpClientFactory.CreateClient().GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(filePath, bytes);
            return webPath;
        }
        catch { return File.Exists(filePath) ? webPath : null; }
    }
}
