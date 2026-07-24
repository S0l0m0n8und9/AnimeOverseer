namespace AnimeOverseer.Server.BackgroundServices;

/// <summary>
/// Coalesces requests for the background runner to check its queue immediately.
/// A single pending signal is sufficient because the runner drains queued jobs
/// one at a time.
/// </summary>
public sealed class SyncJobTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Signal()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(timeout, cancellationToken);
}
