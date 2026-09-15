namespace AnimeOverseer.Server.Services;

public static class CollectionPagingSettings
{
    public const string DefaultKey = "collections.defaultPageSize";
    public const int DefaultPageSize = 24;
    public static readonly int[] PageSizes = [12, 24, 48, 96];
    public static string OverrideKey(int collectionId) => $"collections.{collectionId}.pageSize";
    public static int? Read(string? value) =>
        int.TryParse(value, out var size) && PageSizes.Contains(size) ? size : null;
}
