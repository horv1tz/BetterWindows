using System.Text.Json.Nodes;
using BetterWindows.Core.Desktop;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BetterWindows.Core.Tests.Desktop;

/// <summary>In-memory ISettingsService with the real contract's shape (Changed raised after the write, outside any lock) so the controllers are tested independently of the settings stream.</summary>
public sealed class TestSettings : ISettingsService
{
    private readonly object _gate = new();
    private Settings _current = new();
    public Settings Current { get { lock (_gate) return _current; } }
    public event Action<Settings>? Changed;
    public int Writes { get; private set; }

    public Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default)
    {
        Settings next;
        bool changed;
        lock (_gate)
        {
            next = mutate(_current);
            changed = !ReferenceEquals(next, _current);
            _current = next;
            if (changed) Writes++;
        }
        if (changed) Changed?.Invoke(next);
        return Task.FromResult(next);
    }

    public Task<Settings> PatchAsync(JsonNode patch, CancellationToken ct = default) => throw new NotSupportedException();
    public void Reload() { }

    /// <summary>Synchronous test helper.</summary>
    public void Set(Func<Settings, Settings> f) => UpdateAsync(f).GetAwaiter().GetResult();
}

/// <summary>Programmable file system probe: only the listed paths exist.</summary>
public sealed class FakePathProbe : IPathProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FileExists(string path) => Files.Contains(path);
    public bool DirectoryExists(string path) => Directories.Contains(path);
}

/// <summary>Programmable window tracker (the real one belongs to another stream and is a stub in this clone).</summary>
public sealed class FakeWindowTracker : IWindowTracker
{
    private readonly object _gate = new();
    private WindowSnapshot _snapshot = WindowSnapshot.Empty;
    public string Name => "FakeWindowTracker";
    public WindowSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public IReadOnlyList<TrackedWindow> Windows => Snapshot.Windows;
    public TrackedWindow? Foreground => Snapshot.Foreground;
    public TrackedWindow? Find(long handle) => Snapshot.Windows.FirstOrDefault(w => w.Handle == handle);
    public event Action<WindowSnapshot>? Changed;
    public event Action<TrackedWindow?>? ForegroundChanged;
    public void Start() { }
    public void Stop() { }

    public void Set(IEnumerable<TrackedWindow> windows, long foreground = 0)
    {
        var list = windows.ToArray();
        lock (_gate) _snapshot = new WindowSnapshot(_snapshot.Version + 1, list, list.FirstOrDefault(w => w.Handle == foreground));
    }

    public void RaiseChanged() => Changed?.Invoke(Snapshot);
    public void RaiseForeground(TrackedWindow? w) => ForegroundChanged?.Invoke(w);
    public void Publish(IEnumerable<TrackedWindow> windows, long foreground = 0) { Set(windows, foreground); RaiseChanged(); }

    public static TrackedWindow Win(long handle, string title = "Window", int pid = 100, NativeRect? bounds = null, bool minimized = false, bool cloakedByShell = false,
        bool elevated = false, string appId = "exe:c:\\app\\app.exe", string appName = "App") =>
        new(new RawWindow
        {
            Handle = handle, ProcessId = pid, Title = title, ClassName = "Cls", ExePath = @"C:\app\app.exe", IsVisible = true, IsMinimized = minimized,
            IsCloaked = cloakedByShell, IsCloakedByShell = cloakedByShell, CloakedFlags = cloakedByShell ? 2u : 0u, IsElevated = elevated,
            Bounds = bounds ?? new NativeRect(100, 100, 900, 700),
        }, appId, appName);
}

/// <summary>Records prewarm requests (the real provider belongs to the settings stream).</summary>
public sealed class FakeDynamicResources : IDynamicResourceProvider
{
    public List<long> Prewarmed { get; } = new();
    public Task<DynResource?> GetAsync(string pathAndQuery, CancellationToken ct = default) => Task.FromResult<DynResource?>(null);
    public Task PrewarmWindowThumbnailAsync(long handle, CancellationToken ct = default) { lock (Prewarmed) Prewarmed.Add(handle); return Task.CompletedTask; }
}

public static class Kit
{
    /// <summary>A harness with ONLY controller <typeparamref name="T"/>, an in-memory settings service, a fake tracker/path probe/dynamic resources.</summary>
    public static CoreHarness Harness<T>(Action<IServiceCollection>? more = null) where T : class, IShellController =>
        new(sc =>
        {
            sc.RemoveAll<ISettingsService>();
            sc.AddSingleton<TestSettings>();
            sc.AddSingleton<ISettingsService>(p => p.GetRequiredService<TestSettings>());
            sc.RemoveAll<IWindowTracker>();
            sc.AddSingleton<FakeWindowTracker>();
            sc.AddSingleton<IWindowTracker>(p => p.GetRequiredService<FakeWindowTracker>());
            sc.RemoveAll<IDynamicResourceProvider>();
            sc.AddSingleton<FakeDynamicResources>();
            sc.AddSingleton<IDynamicResourceProvider>(p => p.GetRequiredService<FakeDynamicResources>());
            sc.AddSingleton<FakePathProbe>();
            sc.AddSingleton<IPathProbe>(p => p.GetRequiredService<FakePathProbe>());
            sc.AddSingleton<T>();
            more?.Invoke(sc);
        });

    public static string Code(JsonObject result) => result["ok"]?.GetValue<bool>() == true ? "ok" : (string?)result["error"]?["code"] ?? "?";

    public static MonitorInfo Monitor(string id, int x, int y, int w, int h, bool primary = false, double scale = 1, int workTop = 0, int workBottom = 0) => new()
    {
        Id = id, Primary = primary, Name = id, Scale = scale,
        BoundsPx = new Rect { X = x, Y = y, Width = w, Height = h },
        WorkAreaPx = new Rect { X = x, Y = y + workTop, Width = w, Height = h - workTop - workBottom },
    };

    public static Settings WithDesktop(Settings s, Func<DesktopSettings, DesktopSettings> f) => s with { Desktop = f(s.Desktop) };

    /// <summary>Polls until the condition holds (background work runs on the thread pool).</summary>
    public static async Task Eventually(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new Xunit.Sdk.XunitException("condition not reached in time");
            await Task.Delay(5);
        }
    }
}
