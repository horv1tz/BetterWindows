using System.Text.Json.Nodes;
using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BetterWindows.Core.Tests.MissionControl;

/// <summary>A virtual desktop service whose Switch really changes the current desktop (the shared fake only records), with switches for every failure mode.</summary>
public sealed class ScriptedDesktops : IVirtualDesktopService
{
    public bool IsSupported { get; set; } = true;
    public List<DesktopInfo> Desktops { get; set; } = new()
    {
        new DesktopInfo { Id = "d1", Name = "Desktop 1", Index = 0, Current = true },
        new DesktopInfo { Id = "d2", Name = "Desktop 2", Index = 1 },
        new DesktopInfo { Id = "d3", Name = "Desktop 3", Index = 2 },
    };
    public Dictionary<long, string> WindowDesktop { get; } = new();
    public List<string> Calls { get; } = new();
    public bool SwitchSucceeds { get; set; } = true;
    public bool SwitchUpdatesCurrent { get; set; } = true;
    public bool MoveSucceeds { get; set; } = true;
    public bool CreateSucceeds { get; set; } = true;
    public bool RemoveSucceeds { get; set; } = true;
    public bool ThrowOnWindowQuery { get; set; }
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();

    private string CurrentId => Desktops.FirstOrDefault(d => d.Current)?.Id ?? "";
    public IReadOnlyList<DesktopInfo> GetDesktops() => Desktops.ToArray();
    public string? GetWindowDesktopId(long handle) => ThrowOnWindowQuery ? throw new InvalidOperationException("COM") : WindowDesktop.GetValueOrDefault(handle);
    public bool IsWindowOnCurrentDesktop(long handle) => !WindowDesktop.TryGetValue(handle, out var d) || d == CurrentId;
    public bool Switch(string desktopId)
    {
        Calls.Add("switch:" + desktopId);
        if (!SwitchSucceeds) return false;
        if (SwitchUpdatesCurrent) Desktops = Desktops.Select(d => d with { Current = d.Id == desktopId }).ToList();
        return true;
    }
    public bool MoveWindow(long handle, string desktopId) { Calls.Add($"move:{handle}:{desktopId}"); if (MoveSucceeds) WindowDesktop[handle] = desktopId; return MoveSucceeds; }
    public DesktopInfo? Create()
    {
        Calls.Add("create");
        if (!CreateSucceeds) return null;
        var d = new DesktopInfo { Id = "d" + (Desktops.Count + 1), Name = "Desktop " + (Desktops.Count + 1), Index = Desktops.Count };
        Desktops.Add(d);
        return d;
    }
    public bool Remove(string desktopId) { Calls.Add("remove:" + desktopId); return RemoveSucceeds && Desktops.RemoveAll(d => d.Id == desktopId) > 0; }
}

/// <summary>Delegates to the shared fake but lets a test make Activate/Close fail (UIPI, hung window).</summary>
public sealed class ToggleWindowSystem : IWindowSystem
{
    private readonly FakeWindowSystem _inner;
    public ToggleWindowSystem(FakeWindowSystem inner) { _inner = inner; }
    public bool ActivateSucceeds { get; set; } = true;
    public bool CloseSucceeds { get; set; } = true;
    public IReadOnlyList<RawWindow> EnumerateTopLevel() => _inner.EnumerateTopLevel();
    public RawWindow? TryDescribe(long handle) => _inner.TryDescribe(handle);
    public long GetForegroundWindow() => _inner.GetForegroundWindow();
    public event Action<WindowEvent>? WindowChanged { add { } remove { } }
    public void Start() => _inner.Start();
    public void Stop() => _inner.Stop();
    public bool Activate(long handle) { if (ActivateSucceeds) return _inner.Activate(handle); _inner.Calls.Add(("activate", handle)); return false; }
    public bool Minimize(long handle) => _inner.Minimize(handle);
    public bool Restore(long handle) => _inner.Restore(handle);
    public bool Maximize(long handle) => _inner.Maximize(handle);
    public bool Close(long handle) { if (CloseSucceeds) return _inner.Close(handle); _inner.Calls.Add(("close", handle)); return false; }
    public bool IsFullscreenWindowActive(MonitorInfo monitor) => _inner.IsFullscreenWindowActive(monitor);
    public event Action? FullscreenChanged { add { } remove { } }
    public void Dispose() { }
}

/// <summary>A started MissionControlController with a fake tracker, scripted desktops and every other port faked.</summary>
public sealed class McRig : IDisposable
{
    private McRig(CoreHarness h) { H = h; }
    public CoreHarness H { get; }
    public FakeWindowTracker Tracker => H.Get<FakeWindowTracker>();
    public ScriptedDesktops Desktops => H.Get<ScriptedDesktops>();
    public FakeWindowSystem Windows => H.Get<FakeWindowSystem>();
    public ToggleWindowSystem Toggle => H.Get<ToggleWindowSystem>();
    public FakeSurfaceManager Surfaces => H.Get<FakeSurfaceManager>();
    public FakeInputSimulator Input => H.Get<FakeInputSimulator>();
    public FakeMonitorProvider Monitors => H.Get<FakeMonitorProvider>();
    public FakeCursorProvider Cursor => H.Get<FakeCursorProvider>();
    public FakeDynamicResources Dyn => H.Get<FakeDynamicResources>();
    public OverlayStateService Overlay => H.Get<OverlayStateService>();
    public FakeSurface OverlaySurface { get; private set; } = null!;

    public static McRig Start(Action<McRig>? arrange = null)
    {
        var rig = new McRig(Kit.Harness<MissionControlController>(sc =>
        {
            sc.RemoveAll<IVirtualDesktopService>();
            sc.AddSingleton<ScriptedDesktops>();
            sc.AddSingleton<IVirtualDesktopService>(p => p.GetRequiredService<ScriptedDesktops>());
            sc.RemoveAll<IWindowSystem>();
            sc.AddSingleton<ToggleWindowSystem>();
            sc.AddSingleton<IWindowSystem>(p => p.GetRequiredService<ToggleWindowSystem>());
        }));
        rig.OverlaySurface = rig.H.AddSurface(SurfaceKind.Overlay);
        arrange?.Invoke(rig);
        rig.H.Get<MissionControlController>().Start();
        return rig;
    }

    /// <summary>Registers windows with BOTH the tracker (tracked identity) and the window system (live state), like the real stack.</summary>
    public void Add(params TrackedWindow[] windows)
    {
        Tracker.Set(Tracker.Snapshot.Windows.Concat(windows), Tracker.Snapshot.Foreground?.Handle ?? 0);
        foreach (var w in windows) Windows.Windows.Add(w.Raw);
    }

    public void ShowMissionControl(int token = 1, string monitor = "m1") =>
        Overlay.Set(new OverlayState(true, OverlayMode.MissionControl, monitor, token, null, null));

    public void HideOverlay() => Overlay.Set(OverlayState.Hidden);

    public Task<JsonObject> Call(string type, object? payload = null, FakeSurface? from = null) => H.CallRawAsync(from ?? OverlaySurface, type, payload);

    public async Task<T> Ok<T>(string type, object? payload = null)
    {
        var r = await Call(type, payload);
        r["ok"]!.GetValue<bool>().Should().BeTrue($"{type} failed: {r["error"]}");
        return ProtocolJson.Deserialize<T>(r["payload"])!;
    }

    public async Task<McSnapshot> Snapshot(string? monitorId = null) => await Ok<McSnapshot>(Msg.McSnapshot, new McSnapshotParams { MonitorId = monitorId });

    public void Dispose() => H.Dispose();
}
