namespace BetterWindows.Core.Search;

/// <summary>Picks the English or Russian text for a search locale ("ru" = Russian, anything else = English).</summary>
internal static class Loc
{
    public static string Pick(string locale, string en, string ru) => string.Equals(locale, "ru", StringComparison.Ordinal) ? ru : en;
}

/// <summary>
/// A delay on <see cref="TimeProvider"/> (never Task.Delay, so tests drive it with ManualTimeProvider). Completes after <paramref name="delay"/>; cancelled with
/// the token; the timer is released when the wait ends.
/// </summary>
internal static class TimedWait
{
    public static Task After(TimeProvider time, TimeSpan delay, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
        if (delay <= TimeSpan.Zero) return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = time.CreateTimer(_ => tcs.TrySetResult(), null, delay, Timeout.InfiniteTimeSpan);
        var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        _ = tcs.Task.ContinueWith(_ =>
        {
            registration.Dispose();
            timer.Dispose();
        }, TaskScheduler.Default);
        return tcs.Task;
    }
}

/// <summary>Small string helpers for provider output.</summary>
internal static class TextUtil
{
    /// <summary>The last path segment without its extension ("C:\Apps\msedge.exe" -> "msedge"). Works on Windows-style paths on any OS.</summary>
    public static string FileStem(string path)
    {
        var name = path[(path.LastIndexOf('\\') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>The folder part of a Windows-style path ("C:\a\b.txt" -> "C:\a"); the path itself when it has no separator.</summary>
    public static string ParentFolder(string path)
    {
        int sep = path.LastIndexOf('\\');
        return sep > 2 ? path[..sep] : path;
    }
}
