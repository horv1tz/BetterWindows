using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Status;

/// <summary>
/// Normalises what the platform ports report before it goes on the wire: ranges clamped, text truncated and stripped of control characters, lists capped.
/// The Windows layer is trusted to be roughly right, but a driver returning 250 % brightness or a device with a 10 KB name must never reach the UI (or its CSS) unchecked.
/// Pure functions; every method returns a NEW record and never mutates its argument.
/// </summary>
public static class StatusSanitizer
{
    public const int MaxNameChars = 256;
    public const int MaxIdChars = 512;
    public const int MaxDevices = 64;
    public const int MaxTrayIcons = 128;

    /// <summary>Finite value clamped to 0..1 (NaN/Infinity become 0).</summary>
    public static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    /// <summary>Finite, non-negative seconds (NaN/Infinity/negative become 0).</summary>
    public static double Seconds(double v) => double.IsFinite(v) && v > 0 ? v : 0;

    public static string? Text(string? s, int max = MaxNameChars) => s is null ? null : InputPolicy.Truncate(s, max);

    public static BatteryState Battery(BatteryState s) => s with
    {
        Percent = Math.Clamp(s.Percent, 0, 100),
        MinutesRemaining = s.MinutesRemaining is { } m && m >= 0 ? m : null,
    };

    public static NetworkState Network(NetworkState s) => s with
    {
        SignalBars = Math.Clamp(s.SignalBars, 0, 4),
        Name = Text(s.Name),
    };

    public static AudioState Audio(AudioState s) => s with
    {
        Volume = Clamp01(s.Volume),
        DeviceId = Text(s.DeviceId, MaxIdChars),
        DeviceName = Text(s.DeviceName),
        Devices = (s.Devices ?? Array.Empty<AudioDevice>())
            .Take(MaxDevices)
            .Select(d => d with { Id = Text(d.Id, MaxIdChars) ?? "", Name = Text(d.Name) ?? "" })
            .ToArray(),
    };

    public static BrightnessState Brightness(BrightnessState s) => s with { Level = Clamp01(s.Level) };

    public static BluetoothState Bluetooth(BluetoothState s) => s with
    {
        Devices = (s.Devices ?? Array.Empty<BluetoothDevice>())
            .Take(MaxDevices)
            .Select(d => d with
            {
                Id = Text(d.Id, MaxIdChars) ?? "",
                Name = Text(d.Name) ?? "",
                BatteryPercent = d.BatteryPercent is { } p ? Math.Clamp(p, 0, 100) : null,
            })
            .ToArray(),
    };

    /// <summary>Media metadata with bounded text and sane timeline (position never beyond a known duration). ThumbUrl is NOT touched here: MediaController owns it.</summary>
    public static MediaState Media(MediaState s)
    {
        var duration = Seconds(s.DurationSec);
        var position = Seconds(s.PositionSec);
        if (duration > 0 && position > duration) position = duration;
        return s with
        {
            Title = Text(s.Title),
            Artist = Text(s.Artist),
            Album = Text(s.Album),
            AppName = Text(s.AppName),
            PositionSec = position,
            DurationSec = duration,
        };
    }
}
