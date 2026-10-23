using System.Text.Json.Nodes;
using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BetterWindows.Core.Tests.Desktop;

/// <summary>A started DesktopController on the harness with an in-memory settings service and fakes for every port.</summary>
public sealed class DesktopRig : IDisposable
{
    private DesktopRig(CoreHarness h) { H = h; }
    public CoreHarness H { get; }
    public TestSettings Settings => H.Get<TestSettings>();
    public FakePathProbe Probe => H.Get<FakePathProbe>();
    public FakeMonitorProvider Monitors => H.Get<FakeMonitorProvider>();
    public FakeDesktopIconsController NativeIcons => H.Get<FakeDesktopIconsController>();
    public FakeDesktopFolderSource Folder => H.Get<FakeDesktopFolderSource>();
    public FakeAppLauncher Launcher => H.Get<FakeAppLauncher>();
    public FakeWallpaperService Wallpapers => H.Get<FakeWallpaperService>();
    public IPathGrants Grants => H.Get<IPathGrants>();
    public DesktopController Controller => H.Get<DesktopController>();

    /// <param name="arrange">Runs BEFORE the controller starts (set settings, monitors, folder entries).</param>
    public static async Task<DesktopRig> StartAsync(Action<DesktopRig>? arrange = null, IWallpaperService? wallpaper = null)
    {
        var rig = new DesktopRig(Kit.Harness<DesktopController>(wallpaper is null ? null : sc =>
        {
            sc.RemoveAll<IWallpaperService>();
            sc.AddSingleton(wallpaper);
        }));
        arrange?.Invoke(rig);
        rig.Controller.Start();
        await rig.Controller.Initialized;
        return rig;
    }

    /// <summary>Settings with the desktop layer enabled (the default for most tests) after applying <paramref name="f"/>.</summary>
    public void Configure(Func<DesktopSettings, DesktopSettings> f) => Settings.Set(s => Kit.WithDesktop(s, d => f(d with { Enabled = true })));

    public FakeSurface AddDesktop(string? monitorId = "m1") => H.AddSurface(SurfaceKind.Desktop, monitorId);

    /// <summary>Lets the coalesced state publisher flush.</summary>
    public void Flush() => H.Time.Advance(TimeSpan.FromMilliseconds(60));

    public DesktopState? State(FakeSurface s) { Flush(); return s.LastEvent<DesktopState>(Msg.DesktopState); }

    public Task<JsonObject> Call(FakeSurface from, string type, object? payload = null) => H.CallRawAsync(from, type, payload);

    public async Task<T> Ok<T>(FakeSurface from, string type, object? payload = null)
    {
        var r = await H.CallRawAsync(from, type, payload);
        r["ok"]!.GetValue<bool>().Should().BeTrue($"{type} failed: {r["error"]}");
        return ProtocolJson.Deserialize<T>(r["payload"])!;
    }

    public static DesktopShortcut Shortcut(string id, int x, int y, string? monitor = null, string target = @"C:\Apps\a.exe") =>
        new() { Id = id, Name = id, Target = target, X = x, Y = y, MonitorId = monitor };

    public void Dispose() => H.Dispose();
}
