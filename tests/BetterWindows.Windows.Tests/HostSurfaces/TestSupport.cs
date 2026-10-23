using System.Diagnostics;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Tests.HostSurfaces;

/// <summary>Monitor fixtures in virtual-screen physical pixels. The work area loses 40 px at the bottom (a taskbar-like band).</summary>
internal static class Mon
{
    public static MonitorInfo Make(string id, double x, double y, double w, double h, double scale = 1.0, bool primary = false) => new()
    {
        Id = id,
        Primary = primary,
        Scale = scale,
        BoundsPx = new Rect { X = x, Y = y, Width = w, Height = h },
        WorkAreaPx = new Rect { X = x, Y = y, Width = w, Height = h - 40 },
    };

    public static NativeRect Bounds(int l, int t, int r, int b) => new(l, t, r, b);
}

/// <summary>A clock whose time only moves when a test advances it. Its timers never fire: tests call the coordinator's Tick directly.</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long GetTimestamp() => _ticks;

    public override long TimestampFrequency => Stopwatch.Frequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new InertTimer();

    /// <summary>Returns the timestamp that corresponds to <paramref name="at"/> measured from the clock's origin.</summary>
    public long At(TimeSpan at) => (long)(at.TotalSeconds * Stopwatch.Frequency);

    public void Set(TimeSpan at) => _ticks = At(at);

    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Cursor double for the auto-hide coordinator.</summary>
internal sealed class FakeCursor : ICursorProvider
{
    public NativePoint Position { get; set; }
    public bool Available { get; set; } = true;

    public bool TryGetCursorPosition(out NativePoint position)
    {
        position = Position;
        return Available;
    }

    public bool IsAnyMouseButtonDown() => false;
}
