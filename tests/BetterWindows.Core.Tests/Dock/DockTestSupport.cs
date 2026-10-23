using BetterWindows.Core.Controllers;
using BetterWindows.Core.Dock;
using BetterWindows.Core.Hosting;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Dock;

/// <summary>Programmable app catalog (the real one is owned by core-settings).</summary>
public sealed class FakeAppCatalog : IAppCatalog
{
    public List<AppEntry> Entries { get; } = new();
    public IReadOnlyList<AppEntry> Apps => Entries.ToArray();
    public AppEntry? FindById(string id) => Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    public AppEntry? FindByAumid(string aumid) => Entries.FirstOrDefault(e => string.Equals(e.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
    public AppEntry? FindByExePath(string exePath) => Entries.FirstOrDefault(e => e.Path is not null && string.Equals(e.Path, exePath, StringComparison.OrdinalIgnoreCase));
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();

    public AppEntry Add(string name, string? aumid = null, string? path = null)
    {
        var e = FakeAppCatalogSource.App(name, aumid, path);
        Entries.Add(e);
        return e;
    }
}

/// <summary>In-memory ISettingsService with the contract the controllers rely on: atomic read-modify-write, Changed raised after the write.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    private readonly object _gate = new();
    public Settings Current { get; private set; } = new();
    public int Updates { get; private set; }
    public event Action<Settings>? Changed;
    public Task<Settings> PatchAsync(System.Text.Json.Nodes.JsonNode patch, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default)
    {
        Settings next;
        lock (_gate) { next = mutate(Current); if (!ReferenceEquals(next, Current)) { Current = next; Updates++; } }
        Changed?.Invoke(next);
        return Task.FromResult(next);
    }
    public void Reload() { }
    public void Set(Func<Settings, Settings> mutate) => UpdateAsync(mutate).GetAwaiter().GetResult();
}

public sealed class FakePathProbe : IPathProbe
{
    public Dictionary<string, PathKind> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public PathKind Probe(string path) => Paths.GetValueOrDefault(path, PathKind.Missing);
}

/// <summary>The Core graph for the dock slice: real WindowTracker / AppIdentityResolver / StatePublisher / router, fake ports, fake catalog + settings.</summary>
public sealed class DockHarness : IDisposable
{
    public CoreHarness H { get; }
    public FakeWindowSystem Windows => H.Get<FakeWindowSystem>();
    public FakeAppCatalog Catalog => H.Get<FakeAppCatalog>();
    public FakeSettingsService Settings => H.Get<FakeSettingsService>();
    public FakeAppLauncher Launcher => H.Get<FakeAppLauncher>();
    public FakePathProbe Probe => H.Get<FakePathProbe>();
    public IPathGrants Grants => H.Get<IPathGrants>();
    public IWindowTracker Tracker => H.Get<IWindowTracker>();
    public ManualTimeProvider Time => H.Time;
    public FakeSurface Dock { get; set; } = null!;

    public DockHarness(Action<IServiceCollection>? configure = null)
    {
        H = new CoreHarness(sc =>
        {
            sc.AddSingleton<FakeAppCatalog>();
            sc.AddSingleton<IAppCatalog>(sp => sp.GetRequiredService<FakeAppCatalog>());
            sc.AddSingleton<FakeSettingsService>();
            sc.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<FakeSettingsService>());
            sc.AddSingleton<FakePathProbe>();
            sc.AddSingleton<IPathProbe>(sp => sp.GetRequiredService<FakePathProbe>());
            sc.AddSingleton<IShellController>(sp => ActivatorUtilities.CreateInstance<DockController>(sp));
            sc.AddSingleton<IShellController>(sp => ActivatorUtilities.CreateInstance<ActiveAppController>(sp));
            configure?.Invoke(sc);
        });
        // the stub IWindowTracker/IShellService wiring of AddBetterWindowsCore is the real WindowTracker now
    }

    /// <summary>Starts the tracker (first snapshot) and the controllers, as ShellRuntime does in production.</summary>
    public DockHarness Start()
    {
        H.Get<ShellRuntime>().Start();
        return this;
    }

    /// <summary>Adds a ready dock surface (the publisher pushes the current dock.state to it).</summary>
    public FakeSurface AddDock(string? monitorId = null)
    {
        Dock = H.AddSurface(SurfaceKind.Dock, monitorId);
        return Dock;
    }

    /// <summary>Lets the tracker process pending events and the state publisher flush (event processing 5 ms, Changed 30 ms, dock.state 50 ms).</summary>
    public void Settle() => Time.Advance(TimeSpan.FromMilliseconds(200));

    public DockState? LastDockState(FakeSurface? surface = null) => (surface ?? Dock).LastEvent<DockState>(Msg.DockState);

    /// <summary>Sends a call as the dock surface and returns the error code ("" when it succeeded).</summary>
    public async Task<string> CallAsync(string type, object? payload = null, FakeSurface? from = null)
    {
        var r = await H.CallRawAsync(from ?? Dock, type, payload);
        return r["ok"]?.GetValue<bool>() == true ? "" : (string?)r["error"]?["code"] ?? "?";
    }

    public void Dispose() => H.Dispose();
}

public static class DockAsserts
{
    public static DockItem Item(this DockState s, string id) => s.Items.Single(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
}
