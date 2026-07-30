namespace AnimeOverseer.Server.Services;

/// <summary>Shares the currently dragged catalogue item between the dashboard and header in one Blazor circuit.</summary>
public class CollectionDragService
{
    public int? AnimeId { get; private set; }
    public event Action? Changed;
    public void Start(int animeId) { AnimeId = animeId; Changed?.Invoke(); }
    public void Clear() { AnimeId = null; Changed?.Invoke(); }
}
