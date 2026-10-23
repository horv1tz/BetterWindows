using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Support.Fakes;

// Shared, programmable fakes of EVERY port (frozen — extend by subclassing in your own test folder if you need more).
// Conventions: public mutable state, `Calls` records commands, `Raise…()` fires events synchronously on the caller's thread.

public class FakeWindowSystem : IWindowSystem
{
    public List<RawWindow> Windows { get; } = new();
    public long Foreground { get; set; }
    public bool Started { get; private set; }
    public bool FullscreenActive { get; set; }
    public List<(string Op, long Handle)> Calls { get; } = new();
    public event Action<WindowEvent>? WindowChanged;

    public RawWindow Add(RawWindow w) { Windows.Add(w); return w; }
    public void Remove(long handle) => Windows.RemoveAll(w => w.Handle == handle);
    public void Update(long handle, Func<RawWindow, RawWindow> f) { var i = Windows.FindIndex(w => w.Handle == handle); if (i >= 0) Windows[i] = f(Windows[i]); }
    public void Raise(WindowEventKind kind, long handle) => WindowChanged?.Invoke(new WindowEvent(kind, handle));

    public IReadOnlyList<RawWindow> EnumerateTopLevel() => Windows.ToArray();
    public RawWindow? TryDescribe(long handle) => Windows.FirstOrDefault(w => w.Handle == handle);
    public long GetForegroundWindow() => Foreground;
    public void Start() => Started = true;
    public void Stop() => Started = false;
    public bool Activate(long handle) { Calls.Add(("activate", handle)); Foreground = handle; return Windows.Any(w => w.Handle == handle); }
    public bool Minimize(long handle) { Calls.Add(("minimize", handle)); return true; }
    public bool Restore(long handle) { Calls.Add(("restore", handle)); return true; }
    public bool Maximize(long handle) { Calls.Add(("maximize", handle)); return true; }
    public bool Close(long handle) { Calls.Add(("close", handle)); return true; }
    public bool IsFullscreenWindowActive(MonitorInfo monitor) => FullscreenActive;
    public void Dispose() { }

    /// <summary>A normal visible app window with sensible defaults.</summary>
    public static RawWindow Win(long handle, string title = "Window", string? exe = @"C:\Program Files\App\app.exe", string? aumid = null, int pid = 100) => new()
    {
        Handle = handle, ProcessId = pid, Title = title, ClassName = "TestWindowClass", ExePath = exe, Aumid = aumid, IsVisible = true,
        Bounds = new NativeRect(100, 100, 900, 700),
    };
}

public class FakeMonitorProvider : IMonitorProvider
{
    public List<MonitorInfo> Monitors { get; } = new()
    {
        new MonitorInfo
        {
            Id = "m1", Index = 0, Primary = true, Name = "Display 1", Scale = 1,
            BoundsPx = new Rect { X = 0, Y = 0, Width = 1920, Height = 1080 }, WorkAreaPx = new Rect { X = 0, Y = 0, Width = 1920, Height = 1040 },
        },
    };
    public event Action? MonitorsChanged;
    public void RaiseChanged() => MonitorsChanged?.Invoke();
    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors.ToArray();
    public MonitorInfo? FromPoint(int xPx, int yPx) =>
        Monitors.FirstOrDefault(m => xPx >= m.BoundsPx.X && xPx < m.BoundsPx.X + m.BoundsPx.Width && yPx >= m.BoundsPx.Y && yPx < m.BoundsPx.Y + m.BoundsPx.Height) ?? Monitors.FirstOrDefault();
}

public class FakeCursorProvider : ICursorProvider
{
    public NativePoint Position { get; set; }
    public bool ButtonDown { get; set; }
    public NativePoint GetCursorPosition() => Position;
    public bool IsAnyMouseButtonDown() => ButtonDown;
}

public class FakeTaskbarController : ITaskbarController
{
    public TaskbarMode Current { get; private set; } = TaskbarMode.Visible;
    public List<string> Calls { get; } = new();
    public void Apply(TaskbarMode mode) { Current = mode; Calls.Add("apply:" + mode); }
    public void Restore() { Current = TaskbarMode.Visible; Calls.Add("restore"); }
    public void Dispose() { }
}

public class FakeAppBar : IAppBar
{
    public bool IsRegistered { get; private set; }
    public List<(AppBarEdge Edge, NativeRect Monitor, int Thickness)> Docked { get; } = new();
    public event Action<AppBarNotification>? Notification;
    public void Raise(AppBarNotification n) => Notification?.Invoke(n);
    public NativeRect Dock(AppBarEdge edge, NativeRect monitorBoundsPx, int thicknessPx) { IsRegistered = true; Docked.Add((edge, monitorBoundsPx, thicknessPx)); return monitorBoundsPx; }
    public void Undock() => IsRegistered = false;
    public bool ProcessWindowMessage(uint msg, nint wParam, nint lParam) => false;
    public void Dispose() { }
}

public class FakeAppBarFactory : IAppBarFactory
{
    public List<FakeAppBar> Created { get; } = new();
    public IAppBar Create(nint hwnd) { var b = new FakeAppBar(); Created.Add(b); return b; }
}

public class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    public Dictionary<string, HotkeyGesture> Registered { get; } = new();
    /// <summary>Gestures that are "already taken by another app": TryRegister returns false.</summary>
    public HashSet<HotkeyGesture> Taken { get; } = new();
    public event Action<string>? Pressed;
    public void Press(string id) => Pressed?.Invoke(id);
    public bool TryRegister(string id, HotkeyGesture gesture)
    {
        if (Taken.Contains(gesture)) return false;
        Registered[id] = gesture; return true;
    }
    public void Unregister(string id) => Registered.Remove(id);
    public void UnregisterAll() => Registered.Clear();
    public void Dispose() { }
}

public class FakeInputSimulator : IInputSimulator
{
    public List<string> Calls { get; } = new();
    public void ShowDesktop() => Calls.Add("showDesktop");
    public void SwitchDesktopLeft() => Calls.Add("desktopLeft");
    public void SwitchDesktopRight() => Calls.Add("desktopRight");
    public void SendTaskView() => Calls.Add("taskView");
}

public class FakeVirtualDesktopService : IVirtualDesktopService
{
    public bool IsSupported { get; set; } = true;
    public List<DesktopInfo> Desktops { get; } = new()
    {
        new DesktopInfo { Id = "d1", Name = "Desktop 1", Index = 0, Current = true },
        new DesktopInfo { Id = "d2", Name = "Desktop 2", Index = 1 },
    };
    public Dictionary<long, string> WindowDesktop { get; } = new();
    public List<string> Calls { get; } = new();
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public IReadOnlyList<DesktopInfo> GetDesktops() => Desktops.ToArray();
    public string? GetWindowDesktopId(long handle) => WindowDesktop.GetValueOrDefault(handle);
    public bool IsWindowOnCurrentDesktop(long handle) => !WindowDesktop.TryGetValue(handle, out var d) || d == Desktops.First(x => x.Current).Id;
    public bool Switch(string desktopId) { Calls.Add("switch:" + desktopId); return true; }
    public bool MoveWindow(long handle, string desktopId) { Calls.Add($"move:{handle}:{desktopId}"); WindowDesktop[handle] = desktopId; return true; }
    public DesktopInfo? Create() { Calls.Add("create"); var d = new DesktopInfo { Id = "d" + (Desktops.Count + 1), Name = "Desktop " + (Desktops.Count + 1), Index = Desktops.Count }; Desktops.Add(d); return d; }
    public bool Remove(string desktopId) { Calls.Add("remove:" + desktopId); return Desktops.RemoveAll(d => d.Id == desktopId) > 0; }
}

public class FakeWindowCapture : IWindowCapture
{
    public byte[]? Png { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    public List<long> Requests { get; } = new();
    public Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default) { Requests.Add(handle); return Task.FromResult(Png); }
}

public class FakeTrayHost : ITrayHost
{
    public bool IsSupported { get; set; }
    public List<TrayIcon> IconList { get; } = new();
    public IReadOnlyList<TrayIcon> Icons => IconList.ToArray();
    public List<(string Id, TrayButton Button)> Clicks { get; } = new();
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public void Start() { }
    public void Stop() { }
    public Task<byte[]?> GetIconPngAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
    public void Click(string id, TrayButton button, NativePoint screenPx) => Clicks.Add((id, button));
    public void Dispose() { }
}

public class FakeAppCatalogSource : IAppCatalogSource
{
    public List<AppEntry> Apps { get; } = new();
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AppEntry>>(Apps.ToArray());
    public static AppEntry App(string name, string? aumid = null, string? path = null) => new()
    {
        Id = aumid is not null ? "aumid:" + aumid : "exe:" + path!.ToLowerInvariant(), Name = name, Aumid = aumid, Path = path, Packaged = aumid?.Contains('!') == true,
    };
}

public class FakeIconSource : IIconSource
{
    public byte[]? Png { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    public List<(IconKey Key, int Size)> Requests { get; } = new();
    public Task<byte[]?> GetIconPngAsync(IconKey key, int size, CancellationToken ct = default) { Requests.Add((key, size)); return Task.FromResult(Png); }
}

public class FakeAppLauncher : IAppLauncher
{
    public LaunchResult Result { get; set; } = LaunchResult.Started;
    public List<(LaunchTarget Target, string? Args)> Launched { get; } = new();
    public List<string> Opened { get; } = new();
    public List<string> Revealed { get; } = new();
    public LaunchResult Launch(LaunchTarget target, string? arguments = null) { Launched.Add((target, arguments)); return Result; }
    public bool Open(string target) { Opened.Add(target); return true; }
    public bool RevealInExplorer(string path) { Revealed.Add(path); return true; }
}

public class FakeFileSearch : IFileSearch
{
    public bool IsAvailable { get; set; } = true;
    public List<FileHit> Hits { get; } = new();
    public TimeSpan Delay { get; set; }
    public async IAsyncEnumerable<FileHit> SearchAsync(string query, int limit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        foreach (var h in Hits.Where(h => h.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit)) yield return h;
    }
}

public class FakeAutostartService : IAutostartService
{
    public bool IsEnabled { get; private set; }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}

public class FakeWallpaperService : IWallpaperService
{
    public Dictionary<string, string> Paths { get; } = new();
    public List<(string Path, string? MonitorId)> Set { get; } = new();
    public string? GetWallpaperPath(string monitorId) => Paths.GetValueOrDefault(monitorId);
    public void SetWallpaper(string path, string? monitorId) => Set.Add((path, monitorId));
}

public class FakeDesktopIconsController : IDesktopIconsController
{
    public bool? NativeIconsVisible { get; private set; }
    public int Restores { get; private set; }
    public void SetNativeIconsVisible(bool visible) => NativeIconsVisible = visible;
    public void Restore() { Restores++; NativeIconsVisible = true; }
    public void Dispose() { }
}
