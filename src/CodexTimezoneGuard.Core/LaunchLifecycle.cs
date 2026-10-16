namespace CodexTimezoneGuard.Core;

/// <summary>Normal-close policy for the non-cancellable child-creation and cleanup interval.</summary>
public sealed class LaunchLifecycle
{
    private int inFlight;
    public bool CanCloseNormally => Volatile.Read(ref inFlight) == 0;

    public async Task RunAsync(Func<Task> launch)
    {
        if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0) throw new InvalidOperationException("A launch is already in flight.");
        try { await launch().ConfigureAwait(false); }
        finally { Volatile.Write(ref inFlight, 0); }
    }
}
