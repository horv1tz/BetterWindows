namespace BetterWindows.Native;

/// <summary>One Windows 11 build range of the undocumented virtual desktop COM interfaces.</summary>
public sealed record DesktopBuildRange(int MinBuild, string Label);

/// <summary>
/// OS-build table for the undocumented internal virtual desktop path (used only behind features.internalVirtualDesktops).
/// Ranges are keyed by the lowest build of each Windows 11 release: 22000 (21H2), 22621 (22H2), 26100 (24H2). Windows 10 and unknown builds have no range.
/// The interface identifiers and vtable layouts that would go with each range are NOT in this code base yet: they have to be verified on the
/// matching Windows build before anyone may call them (a wrong vtable call can crash explorer.exe). Until then <see cref="Lookup"/> still identifies the
/// build, and the internal path reports that it has no verified table.
/// </summary>
public static class DesktopBuildTable
{
    /// <summary>Known ranges in ascending order.</summary>
    public static readonly IReadOnlyList<DesktopBuildRange> Ranges = new[]
    {
        new DesktopBuildRange(22000, "Windows 11 21H2"),
        new DesktopBuildRange(22621, "Windows 11 22H2"),
        new DesktopBuildRange(26100, "Windows 11 24H2"),
    };

    /// <summary>The range that contains <paramref name="build"/>, or null for builds below 22000 (Windows 10) or non-positive builds.</summary>
    public static DesktopBuildRange? Lookup(int build)
    {
        if (build <= 0) return null;
        DesktopBuildRange? best = null;
        foreach (var range in Ranges)
        {
            if (build >= range.MinBuild && (best is null || range.MinBuild > best.MinBuild)) best = range;
        }
        return best;
    }
}

/// <summary>Result of one attempt through the internal path.</summary>
public enum DesktopInternalOutcome
{
    /// <summary>The window was moved and the caller may verify it.</summary>
    Moved,
    /// <summary>The internal path worked but refused this request (unknown window, invalid desktop). Does not disable the path.</summary>
    Rejected,
    /// <summary>The internal path itself failed (exception, unexpected HRESULT). Disables the path for the session.</summary>
    Failed,
}

/// <summary>Moves a window through the undocumented internal interfaces. Implementations live behind the build table; production wires none yet.</summary>
public interface IDesktopInternalMover
{
    DesktopInternalOutcome TryMove(nint hwnd, Guid desktopId);
}

/// <summary>
/// Session-wide gate for the internal path. The first <see cref="DesktopInternalOutcome.Failed"/> or exception disables the path until the process
/// restarts, so a broken interface cannot be retried on every call. Thread-safe.
/// </summary>
public sealed class DesktopInternalGate
{
    private int _disabled;
    private readonly Action<string>? _log;

    /// <summary>Creates a gate; <paramref name="log"/> receives one line when the path is disabled.</summary>
    public DesktopInternalGate(Action<string>? log = null) => _log = log;

    /// <summary>True once the path has failed in this session.</summary>
    public bool IsDisabled => Volatile.Read(ref _disabled) != 0;

    /// <summary>Runs <paramref name="operation"/> unless the path is disabled. Returns true only when the window was moved. Never throws.</summary>
    public bool TryRun(Func<DesktopInternalOutcome> operation)
    {
        if (IsDisabled) return false;
        try
        {
            var outcome = operation();
            if (outcome == DesktopInternalOutcome.Failed) Disable("internal call failed");
            return outcome == DesktopInternalOutcome.Moved;
        }
        catch (Exception ex)
        {
            Disable(ex.GetType().Name);
            return false;
        }
    }

    private void Disable(string reason)
    {
        if (Interlocked.Exchange(ref _disabled, 1) != 0) return;
        try { _log?.Invoke("Internal virtual desktop path disabled for this session: " + reason); }
        catch { /* logging must never break the gate */ }
    }
}
