using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Surfaces;

namespace BetterWindows.Native.Tests.HostSurfaces;

public class SurfacePlannerTests
{
    private static readonly IReadOnlyList<MonitorInfo> Two =
        new[] { Mon.Make("1", 0, 0, 1920, 1080, primary: true), Mon.Make("2", 1920, 0, 1920, 1080) };

    private static Settings With(bool dock = true, DockMonitor dockMonitor = DockMonitor.Primary, bool bar = true, bool allBars = true, bool desktop = false) => new()
    {
        Dock = new DockSettings { Enabled = dock, Monitor = dockMonitor },
        MenuBar = new MenuBarSettings { Enabled = bar, AllMonitors = allBars },
        Desktop = new DesktopSettings { Enabled = desktop },
    };

    [Fact]
    public void Primary_dock_keeps_the_monitor_less_id_and_route()
    {
        var plan = SurfacePlanner.Plan(With(bar: false), Two);
        var dock = Assert.Single(plan);
        Assert.Equal(new SurfaceId(SurfaceKind.Dock), dock.Id);
        Assert.Equal("dock", dock.Route);
    }

    [Fact]
    public void Dock_on_all_monitors_gives_the_secondary_dock_its_own_id()
    {
        var docks = SurfacePlanner.Plan(With(dockMonitor: DockMonitor.All, bar: false), Two).Where(p => p.Kind == SurfaceKind.Dock).ToList();
        Assert.Equal(2, docks.Count);
        Assert.Equal(new SurfaceId(SurfaceKind.Dock), docks[0].Id);
        Assert.Equal(new SurfaceId(SurfaceKind.Dock, "2"), docks[1].Id);
        Assert.Equal("dock/2", docks[1].Route);
    }

    [Fact]
    public void Menu_bars_follow_allMonitors_and_disabled_surfaces_are_absent()
    {
        var all = SurfacePlanner.Plan(With(dock: false, allBars: true), Two);
        Assert.Equal(2, all.Count(p => p.Kind == SurfaceKind.Menubar));
        var primaryOnly = SurfacePlanner.Plan(With(dock: false, allBars: false), Two);
        Assert.Equal("1", Assert.Single(primaryOnly).Id.MonitorId);
        Assert.Empty(SurfacePlanner.Plan(With(dock: false, bar: false), Two));
    }

    [Fact]
    public void Desktop_layer_exists_per_monitor_only_when_enabled()
    {
        var desktops = SurfacePlanner.Plan(With(dock: false, bar: false, desktop: true), Two);
        Assert.Equal(new[] { "desktop/1", "desktop/2" }, desktops.Select(d => d.Route).ToArray());
        Assert.Empty(SurfacePlanner.Plan(With(dock: false, bar: false, desktop: false), Two));
    }

    [Fact]
    public void No_monitors_means_an_empty_plan()
    {
        Assert.Empty(SurfacePlanner.Plan(With(), Array.Empty<MonitorInfo>()));
    }

    [Fact]
    public void Diff_creates_keeps_and_removes_without_recreating()
    {
        var plan = SurfacePlanner.Plan(With(dock: true, dockMonitor: DockMonitor.Primary, allBars: true), Two);
        var existing = new[] { new SurfaceId(SurfaceKind.Dock), new SurfaceId(SurfaceKind.Menubar, "1"), new SurfaceId(SurfaceKind.Desktop, "2") };
        var diff = SurfacePlanner.Diff(existing, plan);
        Assert.Equal(new SurfaceId(SurfaceKind.Menubar, "2"), Assert.Single(diff.ToCreate).Id);
        Assert.Equal(2, diff.ToKeep.Count);
        Assert.Equal(new SurfaceId(SurfaceKind.Desktop, "2"), Assert.Single(diff.ToRemove));
    }

    [Fact]
    public void Monitor_less_dock_maps_to_the_primary_monitor()
    {
        Assert.Equal("1", SurfacePlanner.EffectiveMonitorId(new SurfaceId(SurfaceKind.Dock), Two));
        Assert.Equal("2", SurfacePlanner.EffectiveMonitorId(new SurfaceId(SurfaceKind.Dock, "2"), Two));
    }
}

public class VisibilityRegistryTests
{
    [Fact]
    public void Nothing_hidden_by_default_and_a_global_hide_applies_to_every_monitor()
    {
        var r = new VisibilityRegistry();
        Assert.True(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.True(r.Set(null, null, false, "overlay"));
        Assert.False(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.False(r.IsVisible(SurfaceKind.Overlay, "2"));
        Assert.True(r.Set(null, null, true, "overlay"));
        Assert.True(r.IsVisible(SurfaceKind.Dock, "1"));
    }

    [Fact]
    public void A_scoped_hide_only_affects_its_kind_and_monitor()
    {
        var r = new VisibilityRegistry();
        r.Set(SurfaceKind.Dock, "2", false, "fullscreen:2");
        Assert.False(r.IsVisible(SurfaceKind.Dock, "2"));
        Assert.True(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.True(r.IsVisible(SurfaceKind.Menubar, "2"));
    }

    [Fact]
    public void Reasons_stack_and_a_show_removes_only_its_own_reason()
    {
        var r = new VisibilityRegistry();
        r.Set(null, null, false, "b");
        r.Set(null, null, false, "a");
        Assert.Equal(new[] { "a", "b" }, r.Reasons(SurfaceKind.Dock, "1"));
        r.Set(null, null, true, "a");
        Assert.False(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.Equal(new[] { "b" }, r.Reasons(SurfaceKind.Dock, "1"));
    }

    [Fact]
    public void A_show_of_one_kind_does_not_clear_a_broader_hide()
    {
        var r = new VisibilityRegistry();
        r.Set(null, null, false, "x");
        r.Set(SurfaceKind.Dock, null, true, "x");
        Assert.False(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.False(r.IsVisible(SurfaceKind.Menubar, "1"));
    }

    [Fact]
    public void Empty_reason_is_named_unspecified_and_long_reasons_are_truncated()
    {
        var r = new VisibilityRegistry();
        r.Set(null, null, false, null);
        Assert.Equal(new[] { "unspecified" }, r.Reasons(SurfaceKind.Dock, "1"));
        r.Set(null, null, false, new string('r', 500));
        Assert.All(r.Reasons(SurfaceKind.Dock, "1"), reason => Assert.True(reason.Length <= VisibilityRegistry.MaxReasonLength));
    }

    [Fact]
    public void Prefix_removal_drops_only_matching_reasons()
    {
        var r = new VisibilityRegistry();
        r.Set(SurfaceKind.Dock, "1", false, "fullscreen:1");
        r.Set(SurfaceKind.Overlay, null, false, "overlay");
        Assert.True(r.RemoveReasonsWithPrefix("fullscreen:"));
        Assert.True(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.False(r.IsVisible(SurfaceKind.Overlay, "1")); // the other reason survives
    }
}

public class FullscreenSuppressionTests
{
    [Fact]
    public void Fullscreen_on_a_monitor_hides_its_dock_and_menu_bar_but_not_the_overlay()
    {
        var r = new VisibilityRegistry();
        var previous = new HashSet<string>(StringComparer.Ordinal);
        Assert.True(FullscreenSuppression.Apply(r, previous, new[] { "1" }));
        Assert.False(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.False(r.IsVisible(SurfaceKind.Menubar, "1"));
        Assert.True(r.IsVisible(SurfaceKind.Dock, "2"));
        Assert.True(r.IsVisible(SurfaceKind.Overlay, "1"));
        Assert.True(r.IsVisible(SurfaceKind.Desktop, "1"));
        Assert.Equal("fullscreen:1", FullscreenSuppression.Reason("1"));
    }

    [Fact]
    public void Applying_the_same_set_again_changes_nothing()
    {
        var r = new VisibilityRegistry();
        var previous = new HashSet<string>(StringComparer.Ordinal);
        FullscreenSuppression.Apply(r, previous, new[] { "1" });
        Assert.False(FullscreenSuppression.Apply(r, previous, new[] { "1" }));
    }

    [Fact]
    public void A_monitor_leaving_fullscreen_is_shown_again()
    {
        var r = new VisibilityRegistry();
        var previous = new HashSet<string>(StringComparer.Ordinal);
        FullscreenSuppression.Apply(r, previous, new[] { "1", "2" });
        Assert.True(FullscreenSuppression.Apply(r, previous, new[] { "2" }));
        Assert.True(r.IsVisible(SurfaceKind.Dock, "1"));
        Assert.False(r.IsVisible(SurfaceKind.Dock, "2"));
    }
}

public class PendingOverlayRequestTests
{
    private static readonly OverlayRequest First = new() { Mode = OverlayMode.Spotlight };
    private static readonly OverlayRequest Second = new() { Mode = OverlayMode.Launchpad };

    [Fact]
    public void Latest_request_wins_while_the_page_is_not_ready()
    {
        var q = new PendingOverlayRequest();
        Assert.False(q.Enqueue(First, null, TimeSpan.Zero));
        Assert.True(q.Enqueue(Second, new SurfaceId(SurfaceKind.Dock), TimeSpan.FromSeconds(1)));
        Assert.True(q.TryTake(TimeSpan.FromSeconds(2), out var request, out var source, out var expired));
        Assert.Same(Second, request);
        Assert.Equal(SurfaceKind.Dock, source!.Value.Kind);
        Assert.False(expired);
        Assert.False(q.HasRequest);
    }

    [Fact]
    public void A_request_older_than_three_seconds_is_dropped()
    {
        var q = new PendingOverlayRequest();
        q.Enqueue(First, null, TimeSpan.Zero);
        Assert.False(q.TryTake(TimeSpan.FromSeconds(4), out _, out _, out var expired));
        Assert.True(expired);
        Assert.False(q.HasRequest);
    }

    [Fact]
    public void Taking_an_empty_queue_reports_nothing_and_clear_empties_it()
    {
        var q = new PendingOverlayRequest();
        Assert.False(q.TryTake(TimeSpan.Zero, out _, out _, out var expired));
        Assert.False(expired);
        q.Enqueue(First, null, TimeSpan.Zero);
        q.Clear();
        Assert.False(q.HasRequest);
    }
}

public class OverlayActivationPolicyTests
{
    [Fact]
    public void Being_foreground_keeps_the_overlay()
    {
        Assert.Equal(DeactivationDecision.Keep, OverlayActivationPolicy.Decide(true, TimeSpan.FromSeconds(5), 0));
    }

    [Fact]
    public void Within_the_grace_period_activation_is_retried_up_to_the_budget()
    {
        Assert.Equal(DeactivationDecision.RetryActivation, OverlayActivationPolicy.Decide(false, TimeSpan.FromMilliseconds(100), 0));
        Assert.Equal(DeactivationDecision.RetryActivation, OverlayActivationPolicy.Decide(false, TimeSpan.FromMilliseconds(100), 2));
        Assert.Equal(DeactivationDecision.Hide, OverlayActivationPolicy.Decide(false, TimeSpan.FromMilliseconds(100), OverlayActivationPolicy.MaxActivationRetries));
    }

    [Fact]
    public void After_the_grace_period_another_window_really_took_focus()
    {
        Assert.Equal(DeactivationDecision.Hide, OverlayActivationPolicy.Decide(false, TimeSpan.FromSeconds(1), 0));
    }
}

public class RetryBudgetTests
{
    [Fact]
    public void Attempts_are_limited_within_a_sliding_window()
    {
        var b = new RetryBudget(2, TimeSpan.FromSeconds(1));
        Assert.True(b.TryAcquire(TimeSpan.Zero));
        Assert.True(b.TryAcquire(TimeSpan.FromMilliseconds(100)));
        Assert.False(b.TryAcquire(TimeSpan.FromMilliseconds(200)));
        Assert.True(b.TryAcquire(TimeSpan.FromMilliseconds(1100)));
    }

    [Fact]
    public void Reset_gives_the_full_budget_back_and_a_zero_budget_is_at_least_one()
    {
        var b = new RetryBudget(1, TimeSpan.FromSeconds(60));
        Assert.True(b.TryAcquire(TimeSpan.Zero));
        Assert.False(b.TryAcquire(TimeSpan.FromSeconds(1)));
        b.Reset();
        Assert.True(b.TryAcquire(TimeSpan.FromSeconds(1)));
        Assert.True(new RetryBudget(0, TimeSpan.FromSeconds(1)).TryAcquire(TimeSpan.Zero));
    }
}
