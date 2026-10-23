using BetterWindows.Host.Bridge;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Host.Tests.Lifecycle;

public class ShutdownSequencerTests
{
    [Fact]
    public void Steps_run_once_in_declaration_order()
    {
        var order = new List<string>();
        var steps = new[]
        {
            new ShutdownStep("surfaces", () => order.Add("surfaces")),
            new ShutdownStep("controllers", () => order.Add("controllers")),
            new ShutdownStep("taskbar", () => order.Add("taskbar")),
        };
        var seq = new ShutdownSequencer(steps, () => order.Add("watchdog"), watchdog: TimeSpan.FromSeconds(5));

        var result = seq.Run();
        order.Should().Equal("surfaces", "controllers", "taskbar");
        result!.TimedOut.Should().BeFalse();
        result.Steps.Select(s => s.Name).Should().Equal("surfaces", "controllers", "taskbar");
        result.Steps.All(s => s.Succeeded).Should().BeTrue();
        seq.Completed.Should().BeTrue();
    }

    [Fact]
    public void A_throwing_step_is_reported_and_the_next_steps_still_run()
    {
        var order = new List<string>();
        var seq = new ShutdownSequencer(new[]
        {
            new ShutdownStep("first", () => order.Add("first")),
            new ShutdownStep("broken", () => throw new InvalidOperationException("boom")),
            new ShutdownStep("restore", () => order.Add("restore")),
        }, () => { }, watchdog: TimeSpan.FromSeconds(5));

        var result = seq.Run()!;
        order.Should().Equal("first", "restore");
        result.Steps[1].Succeeded.Should().BeFalse();
        result.Steps[1].Error.Should().Be("boom");
        result.Steps[2].Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_second_run_returns_the_first_result_and_runs_nothing_again()
    {
        var count = 0;
        var seq = new ShutdownSequencer(new[] { new ShutdownStep("once", () => count++) }, () => { }, watchdog: TimeSpan.FromSeconds(5));
        var first = seq.Run();
        var second = seq.Run();
        count.Should().Be(1);
        second.Should().BeSameAs(first);
        seq.Started.Should().BeTrue();
    }

    [Fact]
    public void Watchdog_fires_when_the_sequence_hangs_and_the_result_reports_it()
    {
        var fired = new ManualResetEventSlim(false);
        var seq = new ShutdownSequencer(new[]
        {
            new ShutdownStep("hang", () => Thread.Sleep(600)),
            new ShutdownStep("after", () => { }),
        }, () => fired.Set(), watchdog: TimeSpan.FromMilliseconds(100));

        var result = seq.Run()!;
        fired.IsSet.Should().BeTrue("the watchdog must force the restore while the step hangs");
        result.TimedOut.Should().BeTrue();
        result.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Join_on_a_second_caller_waits_for_the_running_sequence()
    {
        var release = new ManualResetEventSlim(false);
        var seq = new ShutdownSequencer(new[] { new ShutdownStep("slow", () => release.Wait(TimeSpan.FromSeconds(5))) }, () => { }, watchdog: TimeSpan.FromSeconds(30));
        var worker = new Thread(() => seq.Run());
        worker.Start();
        SpinWait.SpinUntil(() => seq.Started, TimeSpan.FromSeconds(2));

        seq.Run(TimeSpan.FromMilliseconds(20)).Should().BeNull("the first run is still in progress");
        release.Set();
        worker.Join();
        seq.Run(TimeSpan.FromSeconds(2)).Should().NotBeNull();
    }
}

public class ShutdownCoordinatorTests
{
    [Fact]
    public void Exit_is_asynchronous_and_runs_the_steps_once_with_the_restart_flag_and_code()
    {
        var ui = new ManualUiScheduler();
        var coordinator = new ShutdownCoordinator(ui, TimeProvider.System, NullLogger<ShutdownCoordinator>.Instance);
        var built = new List<bool>();
        var order = new List<string>();
        coordinator.Configure(restart =>
        {
            built.Add(restart);
            return new[] { new ShutdownStep("a", () => order.Add("a")), new ShutdownStep("b", () => order.Add("b")) };
        }, () => { });

        coordinator.RequestExit(restart: true, exitCode: 7);
        coordinator.IsShuttingDown.Should().BeTrue();
        coordinator.RestartRequested.Should().BeTrue();
        coordinator.RequestExit(restart: false, exitCode: 3); // second request: ignored

        WaitUntil(() => ui.Pending > 0);
        ui.RunAll();
        order.Should().Equal("a", "b");
        built.Should().Equal(true);
        coordinator.ExitCode.Should().Be(7);
        coordinator.RestartRequested.Should().BeTrue();

        coordinator.RunNow(TimeSpan.FromSeconds(1));
        order.Should().HaveCount(2, "a second run must not repeat the steps");
    }

    [Fact]
    public void RunNow_on_the_calling_thread_is_synchronous_for_session_ending()
    {
        var ui = new ManualUiScheduler();
        var coordinator = new ShutdownCoordinator(ui, TimeProvider.System, NullLogger<ShutdownCoordinator>.Instance);
        var ran = false;
        coordinator.Configure(_ => new[] { new ShutdownStep("restore", () => ran = true) }, () => { });
        var result = coordinator.RunNow(TimeSpan.FromSeconds(1));
        ran.Should().BeTrue();
        result!.Steps.Should().ContainSingle();
        ui.Pending.Should().Be(0, "the posted request is not needed once the sequence ran");
    }

    [Fact]
    public void Exit_without_configuration_is_harmless()
    {
        var ui = new ManualUiScheduler();
        var coordinator = new ShutdownCoordinator(ui, TimeProvider.System, NullLogger<ShutdownCoordinator>.Instance);
        coordinator.RequestExit(restart: false);
        WaitUntil(() => ui.Pending > 0);
        ui.RunAll();
        coordinator.IsShuttingDown.Should().BeTrue();
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
    }
}
