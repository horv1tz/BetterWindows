using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Resources;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Resources;

/// <summary>A programmable <see cref="IWindowTracker"/> (the real one belongs to another stream): the tests decide which handles are "tracked".</summary>
public sealed class TestWindowTracker : IWindowTracker
{
    private readonly object _gate = new();
    private WindowSnapshot _snapshot = WindowSnapshot.Empty;
    public string Name => "TestWindowTracker";
    public WindowSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public IReadOnlyList<TrackedWindow> Windows => Snapshot.Windows;
    public TrackedWindow? Foreground => Snapshot.Foreground;
    public TrackedWindow? Find(long handle) => Snapshot.Windows.FirstOrDefault(w => w.Handle == handle);
    public event Action<WindowSnapshot>? Changed;
    public event Action<TrackedWindow?>? ForegroundChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }

    public TrackedWindow Add(long handle, string appId = "exe:c:\\app\\app.exe", string name = "App", string? exe = @"C:\app\app.exe", string? aumid = null)
    {
        var w = new TrackedWindow(FakeWindowSystem.Win(handle, name, exe, aumid), appId, name);
        Set(Snapshot.Windows.Append(w).ToArray());
        return w;
    }

    public void Remove(long handle) => Set(Snapshot.Windows.Where(w => w.Handle != handle).ToArray());

    private void Set(IReadOnlyList<TrackedWindow> windows)
    {
        WindowSnapshot s;
        lock (_gate) { _snapshot = new WindowSnapshot(_snapshot.Version + 1, windows, windows.FirstOrDefault()); s = _snapshot; }
        Changed?.Invoke(s);
    }
}

/// <summary>A capture port that records concurrency and can be gated, to test the 3-at-a-time limit and request sharing.</summary>
public sealed class ProgrammableCapture : IWindowCapture
{
    private int _active, _max, _calls;
    private readonly object _gate = new();
    public Func<long, int, byte[]?> Result { get; set; } = (_, _) => Png("thumb");
    public TaskCompletionSource? Gate { get; set; }
    public bool Throw { get; set; }
    public List<(long Handle, int Width)> Requests { get; } = new();
    public int MaxConcurrent => Volatile.Read(ref _max);
    public int Calls => Volatile.Read(ref _calls);

    public async Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default)
    {
        lock (_gate) Requests.Add((handle, maxWidth));
        Interlocked.Increment(ref _calls);
        var now = Interlocked.Increment(ref _active);
        int seen;
        do { seen = Volatile.Read(ref _max); } while (now > seen && Interlocked.CompareExchange(ref _max, now, seen) != seen);
        try
        {
            if (Gate is { } g) await g.Task.WaitAsync(ct);
            if (Throw) throw new InvalidOperationException("PrintWindow hung");
            return Result(handle, maxWidth);
        }
        finally { Interlocked.Decrement(ref _active); }
    }

    /// <summary>PNG signature + a marker, so different captures are distinguishable.</summary>
    public static byte[] Png(string marker) => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(System.Text.Encoding.ASCII.GetBytes(marker)).ToArray();
}

/// <summary>Builds a CoreHarness whose window tracker is a <see cref="TestWindowTracker"/> and returns the router under test.</summary>
public sealed class RouterRig : IDisposable
{
    public CoreHarness H { get; }
    public TestWindowTracker Tracker { get; } = new();
    public FakeIconSource Icons => H.Get<FakeIconSource>();
    public ProgrammableCapture Capture { get; } = new();
    public IDynamicResourceProvider Router => H.Get<IDynamicResourceProvider>();
    public DynamicResourceRouter Concrete => (DynamicResourceRouter)Router;
    public OverlayStateService Overlay => H.Get<OverlayStateService>();
    public string CacheIcons => Path.Combine(H.Paths.CacheDir, "icons");
    private readonly List<string> _cleanup = new();

    public RouterRig(Action<IServiceCollection>? configure = null, bool overlayVisible = true)
    {
        H = new CoreHarness(sc =>
        {
            sc.AddSingleton<IWindowTracker>(Tracker);
            sc.AddSingleton<IWindowCapture>(Capture);
            configure?.Invoke(sc);
        });
        if (overlayVisible) ShowOverlay();
    }

    public void ShowOverlay() => Overlay.Set(new OverlayState(true, OverlayMode.MissionControl, "m1", 1, null, null));
    public void HideOverlay() => Overlay.Set(OverlayState.Hidden);

    public Task<DynResource?> Get(string url) => Router.GetAsync(url);

    /// <summary>
    /// A file or folder that exists for the router's eyes. Core paths must look like "C:\\..." (InputPolicy), which on Linux is simply a file NAME in the working directory
    /// ("C:\\x.png" is a legal Linux file name), so File.Exists/Directory.Exists work on it. Cleaned up on dispose.
    /// </summary>
    public string MakeFile(string name, byte[] content)
    {
        var path = @"C:\" + name;
        File.WriteAllBytes(path, content);
        _cleanup.Add(path);
        return path;
    }

    public string MakeFolder(string name)
    {
        var path = @"C:\" + name;
        Directory.CreateDirectory(path);
        _cleanup.Add(path);
        return path;
    }

    public void Dispose()
    {
        H.Dispose();
        foreach (var p in _cleanup) { try { if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); } catch { /* best effort */ } }
    }

    public static string Unique(string ext) => $"bwtest-{Guid.NewGuid():N}{ext}";
    public static byte[] Jpeg(string marker = "x") => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(System.Text.Encoding.ASCII.GetBytes(marker)).ToArray();
    public static byte[] Png(string marker = "x") => ProgrammableCapture.Png(marker);
}
