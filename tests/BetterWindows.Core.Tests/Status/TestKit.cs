using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Status;

/// <summary>Helpers shared by the core-status tests (Status, Input, Overlay): harness construction, settings with a real merge-patch, a controllable window tracker.</summary>
public static class Kit
{
    /// <summary>A harness that contains only the given controllers (plus a settings service that really applies patches and a controllable window tracker).</summary>
    public static CoreHarness Harness(Action<IServiceCollection>? configure = null, params Type[] controllers)
    {
        return new CoreHarness(sc =>
        {
            sc.AddSingleton<TestSettingsService>();
            sc.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<TestSettingsService>());
            sc.AddSingleton<FakeWindowTracker>();
            sc.AddSingleton<IWindowTracker>(sp => sp.GetRequiredService<FakeWindowTracker>());
            foreach (var type in controllers)
            {
                var t = type;
                sc.AddSingleton<IShellController>(sp => (IShellController)ActivatorUtilities.CreateInstance(sp, t));
            }
            configure?.Invoke(sc);
        });
    }

    public static CoreHarness Harness<TController>(Action<IServiceCollection>? configure = null) where TController : class, IShellController =>
        Harness(configure, typeof(TController));

    /// <summary>Starts every registered controller (what ShellRuntime does) and returns the one of the requested type.</summary>
    public static T StartAll<T>(this CoreHarness h) where T : class, IShellController
    {
        var all = h.Services.GetServices<IShellController>().ToArray();
        foreach (var c in all) c.Start();
        return all.OfType<T>().Single();
    }

    public static TestSettingsService Settings(this CoreHarness h) => h.Get<TestSettingsService>();

    /// <summary>Marks a surface that was added with ready:false as ready (the same moment surface.ready completes) so initial state is pushed.</summary>
    public static FakeSurface Ready(this CoreHarness h, FakeSurface s)
    {
        h.Registry.MarkReadyForTests(s);
        return s;
    }

    /// <summary>Polls until <paramref name="condition"/> holds (for work that deliberately runs on the thread pool).</summary>
    public static async Task Eventually(Func<bool> condition, int timeoutMs = 5000, string? because = null)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new Xunit.Sdk.XunitException("Condition not met in time" + (because is null ? "" : ": " + because));
            await Task.Delay(5);
        }
    }

    /// <summary>Waits a little to prove that something does NOT happen on the thread pool.</summary>
    public static Task Settle(int ms = 80) => Task.Delay(ms);

    public static JsonObject? ErrorOf(JsonObject result) => result["error"] as JsonObject;
    public static string? ErrorCode(JsonObject result) => (string?)ErrorOf(result)?["code"];
    public static bool IsOk(JsonObject result) => result["ok"]?.GetValue<bool>() == true;
}

/// <summary>An <see cref="ISettingsService"/> with real RFC 7386 merge-patch semantics (the stub in Core only mutates in memory); records every patch.</summary>
public sealed class TestSettingsService : ISettingsService
{
    private readonly object _gate = new();
    public Settings Current { get; private set; } = new();
    public event Action<Settings>? Changed;
    public List<JsonNode> Patches { get; } = new();

    public Task<Settings> PatchAsync(JsonNode patch, CancellationToken ct = default)
    {
        Patches.Add(patch.DeepClone());
        Settings next;
        lock (_gate)
        {
            var doc = JsonSerializer.SerializeToNode(Current, ProtocolJson.Options)!;
            Merge(doc, patch);
            next = doc.Deserialize<Settings>(ProtocolJson.Options)!;
            Current = next;
        }
        Changed?.Invoke(next);
        return Task.FromResult(next);
    }

    public Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default)
    {
        Settings next;
        lock (_gate) { next = mutate(Current); Current = next; }
        Changed?.Invoke(next);
        return Task.FromResult(next);
    }

    public void Reload() { }

    /// <summary>Synchronous convenience for tests.</summary>
    public Settings Set(Func<Settings, Settings> mutate) => UpdateAsync(mutate).GetAwaiter().GetResult();

    private static void Merge(JsonNode target, JsonNode patch)
    {
        if (target is not JsonObject t || patch is not JsonObject p) return;
        foreach (var (key, value) in p.ToArray())
        {
            if (value is null) { t.Remove(key); continue; }
            if (value is JsonObject && t[key] is JsonObject child) Merge(child, value);
            else t[key] = value.DeepClone();
        }
    }
}

/// <summary>An <see cref="IWindowTracker"/> whose foreground the test controls; raises the same events as the real tracker.</summary>
public sealed class FakeWindowTracker : IWindowTracker
{
    private long _version;
    public string Name => "FakeWindowTracker";
    public WindowSnapshot Snapshot { get; private set; } = WindowSnapshot.Empty;
    public IReadOnlyList<TrackedWindow> Windows => Snapshot.Windows;
    public TrackedWindow? Foreground => Snapshot.Foreground;
    public TrackedWindow? Find(long handle) => Snapshot.Windows.FirstOrDefault(w => w.Handle == handle);
    public event Action<WindowSnapshot>? Changed;
    public event Action<TrackedWindow?>? ForegroundChanged;
    public void Start() { }
    public void Stop() { }

    public TrackedWindow SetForeground(long handle, string? appId = null)
    {
        var window = new TrackedWindow(FakeWindowSystem.Win(handle, "Window " + handle), appId ?? "exe:c:\\apps\\app" + handle + ".exe", "App " + handle);
        Snapshot = new WindowSnapshot(++_version, new[] { window }, window);
        Changed?.Invoke(Snapshot);
        ForegroundChanged?.Invoke(window);
        return window;
    }

    /// <summary>Foreground becomes "nothing" (desktop focused / own window): ForegroundChanged(null) is raised.</summary>
    public void ClearForeground()
    {
        Snapshot = new WindowSnapshot(++_version, Snapshot.Windows, null);
        Changed?.Invoke(Snapshot);
        ForegroundChanged?.Invoke(null);
    }
}

/// <summary>A hotkey registrar that logs the exact sequence of calls and can be told which gestures are taken or throw.</summary>
public sealed class RecordingHotkeyRegistrar : IHotkeyRegistrar
{
    private readonly object _gate = new();
    public Dictionary<string, HotkeyGesture> Registered { get; } = new();
    public HashSet<HotkeyGesture> Taken { get; } = new();
    public List<string> Log { get; } = new();
    public bool ThrowOnRegister { get; set; }
    public event Action<string>? Pressed;
    public void Press(string id) => Pressed?.Invoke(id);
    public bool HasPressedSubscribers => Pressed is not null;

    public bool TryRegister(string id, HotkeyGesture gesture)
    {
        lock (_gate)
        {
            Log.Add($"register:{id}:{gesture.Modifiers}:{gesture.VirtualKey}");
            if (ThrowOnRegister) throw new InvalidOperationException("boom");
            if (Taken.Contains(gesture) || Registered.Any(kv => kv.Key != id && kv.Value == gesture)) return false;
            Registered[id] = gesture;
            return true;
        }
    }

    public void Unregister(string id)
    {
        lock (_gate)
        {
            Log.Add("unregister:" + id);
            Registered.Remove(id);
        }
    }

    public void UnregisterAll() { lock (_gate) { Log.Add("unregisterAll"); Registered.Clear(); } }
    public void Dispose() { }
    public string[] Snapshot() { lock (_gate) return Log.ToArray(); }
}

/// <summary>A tray host that records Start/Stop/Click (including the screen position).</summary>
public sealed class TestTrayHost : ITrayHost
{
    private int _starts, _stops;
    public bool IsSupported { get; set; }
    public List<TrayIcon> IconList { get; } = new();
    public IReadOnlyList<TrayIcon> Icons { get { lock (IconList) return IconList.ToArray(); } }
    public List<(string Id, TrayButton Button, NativePoint Position)> Clicks { get; } = new();
    public bool ThrowOnClick { get; set; }
    public int Starts => Volatile.Read(ref _starts);
    public int Stops => Volatile.Read(ref _stops);
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public void Start() => Interlocked.Increment(ref _starts);
    public void Stop() => Interlocked.Increment(ref _stops);
    public Task<byte[]?> GetIconPngAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    public void Click(string id, TrayButton button, NativePoint screenPx)
    {
        if (ThrowOnClick) throw new InvalidOperationException("boom");
        Clicks.Add((id, button, screenPx));
    }
    public void Dispose() { }
}

/// <summary>A surface manager whose ShowOverlay takes effect LATER (like a Host that marshals to its UI thread): <see cref="Commit"/> applies the pending show.</summary>
public sealed class LaggingSurfaceManager : ISurfaceManager
{
    private (OverlayRequest Request, SurfaceId? Source)? _pending;
    public List<(OverlayRequest Request, SurfaceId? Source)> Shown { get; } = new();
    public List<(SurfaceId Dock, bool Revealed)> DockReveals { get; } = new();
    public int HideCount { get; private set; }
    public bool IsOverlayVisible { get; private set; }
    public OverlayMode? CurrentOverlayMode { get; private set; }
    public int OverlayToken { get; private set; }
    public bool SupportsGlassOverflow => false;
    public event Action? OverlayHidden;
    public void Commit()
    {
        if (_pending is not { } p) return;
        _pending = null;
        IsOverlayVisible = true;
        CurrentOverlayMode = p.Request.Mode;
        OverlayToken++;
    }
    public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) { }
    public void ShowOverlay(OverlayRequest request, SurfaceId? source) { Shown.Add((request, source)); _pending = (request, source); }
    public void HideOverlay() { HideCount++; IsOverlayVisible = false; CurrentOverlayMode = null; }
    public void SetDockRevealed(SurfaceId dock, bool revealed) => DockReveals.Add((dock, revealed));
    public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) { }
    public void ShowSettingsWindow(string? page) { }
    public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) { }
    public void RaiseHostHidden() { IsOverlayVisible = false; CurrentOverlayMode = null; OverlayHidden?.Invoke(); }
}

/// <summary>A cursor that counts how often it was polled (hot-corner polling must stop when disabled).</summary>
public sealed class CountingCursor : ICursorProvider
{
    private int _polls;
    public NativePoint Position { get; set; }
    public bool ButtonDown { get; set; }
    public bool Unavailable { get; set; }
    public int Polls => Volatile.Read(ref _polls);
    public bool TryGetCursorPosition(out NativePoint position) { Interlocked.Increment(ref _polls); position = Position; return !Unavailable; }
    public bool IsAnyMouseButtonDown() => ButtonDown;
}
