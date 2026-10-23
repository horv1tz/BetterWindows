using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterWindows.Native;

/// <summary>Written (atomically) BEFORE Explorer's icon list is hidden; deleted after it is shown again. Its existence is the "we hid it" marker.</summary>
internal sealed record DesktopIconsMarker(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("hiddenAtUtc")] DateTime HiddenAtUtc);

/// <summary>The state to restore (state\desktopicons.json): whether the list was visible before we touched it.</summary>
internal sealed record DesktopIconsSnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("listViewWasVisible")] bool ListViewWasVisible,
    [property: JsonPropertyName("listViewHwnd")] long ListViewHwnd,
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("capturedUtc")] DateTime CapturedUtc);

/// <summary>
/// Persistence of the "desktop icons hidden by BetterWindows" state under <c>%LOCALAPPDATA%\BetterWindows\state</c>. Two small JSON files:
/// <c>desktop-icons-hidden</c> (marker) and <c>desktopicons.json</c> (snapshot). Writes are atomic (temp file + move) so a crash never leaves a half-written file.
/// A fresh process can read them and undo the change (<see cref="DesktopIconsRestorer.RestoreNow"/>). No dependencies on any other service.
/// </summary>
internal sealed class DesktopIconsState
{
    public const int CurrentVersion = 1;
    public const string MarkerFileName = "desktop-icons-hidden";
    public const string SnapshotFileName = "desktopicons.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public DesktopIconsState(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }
    public string MarkerPath => Path.Combine(Directory, MarkerFileName);
    public string SnapshotPath => Path.Combine(Directory, SnapshotFileName);

    /// <summary>The standard state folder of the app.</summary>
    public static DesktopIconsState CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new DesktopIconsState(Path.Combine(local, "BetterWindows", "state"));
    }

    public bool MarkerExists
    {
        get
        {
            try { return File.Exists(MarkerPath); }
            catch (Exception) { return false; }
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>Tolerant parse: null for empty/garbage content (a corrupt state file must not stop the restore).</summary>
    public static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Writes snapshot then marker. Returns false if either cannot be persisted (the caller then must NOT hide anything).</summary>
    public bool TryBegin(DesktopIconsSnapshot snapshot)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            WriteAtomic(SnapshotPath, Serialize(snapshot));
            WriteAtomic(MarkerPath, Serialize(new DesktopIconsMarker(CurrentVersion, Environment.ProcessId, DateTime.UtcNow)));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public DesktopIconsSnapshot? ReadSnapshot()
    {
        try { return File.Exists(SnapshotPath) ? Deserialize<DesktopIconsSnapshot>(File.ReadAllText(SnapshotPath)) : null; }
        catch (Exception) { return null; }
    }

    public DesktopIconsMarker? ReadMarker()
    {
        try { return File.Exists(MarkerPath) ? Deserialize<DesktopIconsMarker>(File.ReadAllText(MarkerPath)) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Removes both files (marker last, so an interrupted cleanup still reads as "needs restore"). Never throws.</summary>
    public void Clear()
    {
        TryDelete(SnapshotPath);
        TryDelete(MarkerPath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* best effort; a leftover marker just causes one harmless extra restore */ }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
