using BetterWindows.Host.Lifecycle;

namespace BetterWindows.Host.Tests.Lifecycle;

public class ShutdownPlanTests
{
    private static ShutdownActions Actions(List<string> log) => new(
        StopSurfaces: () => log.Add("surfaces"),
        StopControllers: () => log.Add("controllers"),
        RestoreTaskbar: () => log.Add("taskbar"),
        RestoreDesktopIcons: () => log.Add("desktop"),
        DisposeTray: () => log.Add("tray"),
        DisposeWebView: () => log.Add("webview"),
        MarkCleanExit: () => log.Add("clean"),
        ReleaseInstance: () => log.Add("mutex"),
        Relaunch: () => log.Add("relaunch"),
        ExitApplication: () => log.Add("exit"));

    [Fact]
    public void Normal_exit_stops_the_surfaces_before_the_controllers_and_restores_the_shell_before_exiting()
    {
        var log = new List<string>();
        var steps = ShutdownPlan.Build(Actions(log), relaunch: false);
        foreach (var s in steps) s.Run();
        log.Should().Equal("surfaces", "controllers", "taskbar", "desktop", "tray", "webview", "clean", "mutex", "exit");
        steps.Select(s => s.Name).Should().Equal(
            ShutdownPlan.Surfaces, ShutdownPlan.Controllers, ShutdownPlan.Taskbar, ShutdownPlan.DesktopIcons, ShutdownPlan.Tray,
            ShutdownPlan.WebView, ShutdownPlan.StartupState, ShutdownPlan.SingleInstance, ShutdownPlan.Application);
    }

    [Fact]
    public void The_shell_is_restored_before_the_last_step_and_the_mutex_is_released_before_any_relaunch()
    {
        var log = new List<string>();
        var steps = ShutdownPlan.Build(Actions(log), relaunch: true);
        foreach (var s in steps) s.Run();
        log.IndexOf("taskbar").Should().BeLessThan(log.IndexOf("exit"));
        log.IndexOf("desktop").Should().BeLessThan(log.IndexOf("exit"));
        log.IndexOf("mutex").Should().BeLessThan(log.IndexOf("relaunch"), "the new process must find the mutex free");
        log.Last().Should().Be("exit");
        steps[^2].Name.Should().Be(ShutdownPlan.Relaunch);
    }

    [Fact]
    public void Relaunch_is_only_planned_for_a_restart()
    {
        ShutdownPlan.Build(Actions(new List<string>()), relaunch: false).Select(s => s.Name).Should().NotContain(ShutdownPlan.Relaunch);
        ShutdownPlan.Build(Actions(new List<string>()), relaunch: true).Select(s => s.Name).Should().Contain(ShutdownPlan.Relaunch);
    }

    [Fact]
    public void Run_through_the_sequencer_keeps_going_when_one_step_throws()
    {
        var log = new List<string>();
        var actions = Actions(log) with { DisposeTray = () => throw new InvalidOperationException("tray gone") };
        var result = new ShutdownSequencer(ShutdownPlan.Build(actions, relaunch: false), () => { }, watchdog: TimeSpan.FromSeconds(5)).Run()!;
        result.Steps.Single(s => s.Name == ShutdownPlan.Tray).Succeeded.Should().BeFalse();
        log.Should().Contain("taskbar").And.Contain("exit", "a failing tray step must not skip the restore or the exit");
    }
}
