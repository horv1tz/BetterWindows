using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Native;

/// <summary>One taskbar window (Shell_TrayWnd = primary, Shell_SecondaryTrayWnd = one per additional monitor).</summary>
/// <param name="Handle">HWND value.</param>
/// <param name="IsPrimary">True for Shell_TrayWnd.</param>
/// <param name="IsVisible">IsWindowVisible.</param>
internal readonly record struct TaskbarWindowInfo(nint Handle, bool IsPrimary, bool IsVisible);

/// <summary>
/// The shell's taskbar as the controller sees it. Abstracted so the state machine (snapshot-before-mutation, hide/auto-hide/restore,
/// stale-marker recovery) is unit-tested on any OS; <see cref="Win32TaskbarSurface"/> is the real implementation.
/// </summary>
internal interface ITaskbarSurface
{
    /// <summary>Primary taskbar first, then the secondary ones. Empty when Explorer's taskbar does not exist (Explorer not running).</summary>
    IReadOnlyList<TaskbarWindowInfo> GetWindows();

    /// <summary>ABM_GETSTATE: ABS_AUTOHIDE (1) | ABS_ALWAYSONTOP (2). False when there is no taskbar.</summary>
    bool TryGetState(out uint state);

    /// <summary>ABM_SETSTATE, verified by reading the state back.</summary>
    bool TrySetState(uint state);

    /// <summary>Shows (SW_SHOWNA) or hides (SW_HIDE) one taskbar window and waits briefly for the change to land. False if it did not.</summary>
    bool SetVisible(nint handle, bool visible);
}

/// <summary>The ORIGINAL taskbar state captured before the first mutation; persisted so a fresh process can undo everything.</summary>
internal sealed record TaskbarSnapshot
{
    /// <summary>Schema version.</summary>
    [JsonPropertyName("version")] public int Version { get; init; } = 1;

    /// <summary>False if ABM_GETSTATE could not be read; then only visibility is restored.</summary>
    [JsonPropertyName("stateKnown")] public bool StateKnown { get; init; }

    /// <summary>ABM_GETSTATE bits (ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2).</summary>
    [JsonPropertyName("state")] public uint State { get; init; }

    /// <summary>The primary taskbar window was visible.</summary>
    [JsonPropertyName("primaryVisible")] public bool PrimaryVisible { get; init; } = true;

    /// <summary>Every secondary taskbar window was visible (true when there were none).</summary>
    [JsonPropertyName("secondaryVisible")] public bool SecondaryVisible { get; init; } = true;

    /// <summary>Process that took the snapshot (diagnostics only).</summary>
    [JsonPropertyName("processId")] public int ProcessId { get; init; }

    /// <summary>When the snapshot was taken (diagnostics only).</summary>
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; init; }
}

/// <summary>Persistence of the single <see cref="TaskbarSnapshot"/> marker.</summary>
internal interface ITaskbarSnapshotStore
{
    /// <summary>True when a marker file exists (readable or not).</summary>
    bool Exists { get; }

    /// <summary>The marker, or null when absent. <paramref name="corrupt"/> is true when a file exists but cannot be interpreted.</summary>
    TaskbarSnapshot? TryRead(out bool corrupt);

    /// <summary>Atomically writes the marker (temp file + rename, flushed to disk). False on any I/O failure.</summary>
    bool TryWrite(TaskbarSnapshot snapshot);

    /// <summary>Deletes the marker; never throws.</summary>
    void Delete();
}

/// <summary>Marker file store: <c>%LOCALAPPDATA%\BetterWindows\state\taskbar.json</c> (or any path for tests).</summary>
internal sealed class FileTaskbarSnapshotStore : ITaskbarSnapshotStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    /// <param name="path">Full file path of the marker.</param>
    public FileTaskbarSnapshotStore(string path)
    {
        _path = path;
    }

    /// <summary>The production location.</summary>
    public static FileTaskbarSnapshotStore Default { get; } = new(DefaultPath());

    /// <summary>%LOCALAPPDATA%\BetterWindows\state\taskbar.json.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWindows", "state", "taskbar.json");

    /// <inheritdoc />
    public bool Exists => File.Exists(_path);

    /// <inheritdoc />
    public TaskbarSnapshot? TryRead(out bool corrupt)
    {
        corrupt = false;
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var snap = JsonSerializer.Deserialize<TaskbarSnapshot>(stream, Json);
            if (snap == null || snap.Version != 1)
            {
                corrupt = true;
                return null;
            }

            return snap;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            corrupt = true;
            return null;
        }
    }

    /// <inheritdoc />
    public bool TryWrite(TaskbarSnapshot snapshot)
    {
        string tmp = _path + "." + Environment.ProcessId + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, snapshot, Json);
                stream.Flush(flushToDisk: true);
            }

            // Rename over the old file: readers see either the old or the new complete file, never a torn one.
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception)
            {
                // Nothing more to do.
            }

            return false;
        }
    }

    /// <inheritdoc />
    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: the next restore simply finds an already-restored taskbar and removes it.
        }
    }
}
