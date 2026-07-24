using Microsoft.Data.Sqlite;

namespace AnimeOverseer.Server.Services;

public sealed record StorageUsage(long DatabaseBytes, long ImageCacheBytes, int ImageCount);

public class StorageUsageService(IConfiguration configuration, IWebHostEnvironment environment)
{
    public Task<StorageUsage> GetUsageAsync()
    {
        var databaseBytes = GetDatabaseSize();
        var cacheDirectory = Path.Combine(environment.WebRootPath, "images", "cache");
        var imageFiles = Directory.Exists(cacheDirectory)
            ? Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();

        var imageBytes = 0L;
        var imageCount = 0;
        foreach (var file in imageFiles)
        {
            try
            {
                imageBytes += new FileInfo(file).Length;
                imageCount++;
            }
            catch (IOException)
            {
                // A cache image may be replaced while usage is being calculated.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore an inaccessible cache file rather than making Settings unusable.
            }
        }

        return Task.FromResult(new StorageUsage(databaseBytes, imageBytes, imageCount));
    }

    private long GetDatabaseSize()
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=animeoverseer.db";
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:") return 0;

        var databasePath = Path.IsPathFullyQualified(dataSource)
            ? dataSource
            : Path.Combine(environment.ContentRootPath, dataSource);

        // SQLite can store recent changes in these companion files while WAL mode is active.
        return new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
    }
}
