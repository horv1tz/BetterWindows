using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>SMTC timeline arithmetic, AUMID-derived app names and command mapping (unit-tested on any OS).</summary>
internal static class SmtcMath
{
    /// <summary>GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing.</summary>
    public const int PlaybackPlaying = 4;

    /// <summary>100 ns ticks per second (SMTC seek position unit).</summary>
    public const long TicksPerSecond = 10_000_000;

    public static bool IsPlaying(int playbackStatus) => playbackStatus == PlaybackPlaying;

    /// <summary>Duration in seconds from the timeline's start and end; never negative.</summary>
    public static double DurationSeconds(TimeSpan start, TimeSpan end)
    {
        var d = (end - start).TotalSeconds;
        return double.IsNaN(d) || d < 0 ? 0.0 : d;
    }

    /// <summary>
    /// Current position. SMTC reports <paramref name="position"/> as of <paramref name="lastUpdated"/>; while playing, the time since then is added.
    /// The result is clamped to 0..duration (duration 0 means unknown: no upper clamp).
    /// </summary>
    public static double PositionSeconds(TimeSpan position, DateTimeOffset lastUpdated, bool playing, DateTimeOffset now, double durationSeconds)
    {
        var elapsed = 0.0;
        if (playing && lastUpdated > DateTimeOffset.MinValue && now > lastUpdated)
            elapsed = (now - lastUpdated).TotalSeconds;
        var pos = Math.Max(0.0, position.TotalSeconds) + elapsed;
        if (double.IsNaN(pos) || double.IsInfinity(pos)) pos = 0.0;
        if (durationSeconds > 0) pos = Math.Min(pos, durationSeconds);
        return Math.Max(0.0, pos);
    }

    /// <summary>
    /// Seek position in 100 ns ticks for TryChangePlaybackPositionAsync. Null for NaN, infinite or negative requests (the UI is untrusted; nothing is sent).
    /// </summary>
    public static long? SeekTicks(double? positionSec)
    {
        if (positionSec is not { } s || double.IsNaN(s) || double.IsInfinity(s) || s < 0) return null;
        var ticks = s * TicksPerSecond;
        return ticks >= long.MaxValue ? null : (long)ticks;
    }

    /// <summary>
    /// A readable name from a SourceAppUserModelId: "Spotify.exe" -> "Spotify"; a packaged id "Publisher.App_hash!App" -> "App"-style name taken
    /// from the package family part ("Microsoft.ZuneMusic_8wekyb3d8bbwe!App" -> "ZuneMusic"); a path -> its file name without extension.
    /// Returns null for empty input.
    /// </summary>
    public static string? AppNameFromAumid(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        var s = aumid.Trim();
        var bang = s.IndexOf('!');
        if (bang >= 0)
        {
            // Packaged app: the family name is "Publisher.Name_publisherHash"; the readable part is the segment after the last dot of the family name.
            var family = s[..bang];
            var underscore = family.IndexOf('_');
            if (underscore >= 0) family = family[..underscore];
            var dot = family.LastIndexOf('.');
            var name = dot >= 0 && dot < family.Length - 1 ? family[(dot + 1)..] : family;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        var file = s.Replace('/', '\\');
        var slash = file.LastIndexOf('\\');
        if (slash >= 0 && slash < file.Length - 1) file = file[(slash + 1)..];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    public static bool Same(MediaState a, MediaState b) =>
        a.Available == b.Available && a.Title == b.Title && a.Artist == b.Artist && a.Album == b.Album && a.AppName == b.AppName
        && a.Playing == b.Playing && a.PositionSec == b.PositionSec && a.DurationSec == b.DurationSec && a.ThumbUrl == b.ThumbUrl
        && a.CanPlay == b.CanPlay && a.CanPause == b.CanPause && a.CanNext == b.CanNext && a.CanPrevious == b.CanPrevious && a.CanSeek == b.CanSeek;
}
