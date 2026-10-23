using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Host.Tests.Lifecycle;

/// <summary>Hotkey status the test flips by hand.</summary>
internal sealed class FakeHotkeyStatus : IHotkeyStatus
{
    public bool EscapeHotkeyRegistered { get; set; }
    public IReadOnlyList<HotkeyRegistration> Items { get; } = Array.Empty<HotkeyRegistration>();
    public event Action? Changed;
    public void Report(HotkeyRegistration registration) { }
    public void Clear(string id) { }
    public void Raise() => Changed?.Invoke();
}

public class ShellSafetyGateTests
{
    private static (ShellSafetyGate gate, SurfaceRegistry registry, FakeHotkeyStatus hotkeys) Build(HostRuntimeOptions options)
    {
        var registry = new SurfaceRegistry();
        var hotkeys = new FakeHotkeyStatus();
        var gate = new ShellSafetyGate(options, registry, hotkeys, new GuardianLauncher(NullLogger<GuardianLauncher>.Instance), NullLogger<ShellSafetyGate>.Instance);
        return (gate, registry, hotkeys);
    }

    private static HostRuntimeOptions Normal() => new(CommandLineParser.Parse(Array.Empty<string>()));

    private static void MakeReady(SurfaceRegistry registry, SurfaceKind kind)
    {
        var channel = new FakeChannel(new SurfaceId(kind));
        registry.Register(channel);
        registry.MarkReadyForTests(channel);
    }

    [Fact]
    public void Hiding_is_not_ready_until_dock_and_menubar_are_ready_and_an_escape_hotkey_exists()
    {
        var (gate, registry, hotkeys) = Build(Normal());
        gate.IsReadyToHide(out var reason).Should().BeFalse();
        reason.Should().Contain("dock");

        MakeReady(registry, SurfaceKind.Dock);
        gate.IsReadyToHide(out reason).Should().BeFalse();
        reason.Should().Contain("menu bar");

        MakeReady(registry, SurfaceKind.Menubar);
        gate.IsReadyToHide(out reason).Should().BeFalse();
        reason.Should().Contain("escape hotkey");

        hotkeys.EscapeHotkeyRegistered = true;
        gate.IsReadyToHide(out reason).Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void Readiness_changes_are_announced_through_ReadyChanged()
    {
        var (gate, registry, hotkeys) = Build(Normal());
        var raised = 0;
        gate.ReadyChanged += () => raised++;
        MakeReady(registry, SurfaceKind.Dock);
        MakeReady(registry, SurfaceKind.Menubar);
        hotkeys.EscapeHotkeyRegistered = true;
        hotkeys.Raise();
        raised.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Smoke_safe_mode_and_no_hide_block_hiding_with_a_reason()
    {
        var smoke = new HostRuntimeOptions(CommandLineParser.Parse(new[] { "--smoke" }));
        Build(smoke).gate.BlockReason.Should().Be("smoke mode");
        Build(smoke).gate.HidingBlocked.Should().BeTrue();

        var safe = new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()), crashLoopSafeMode: true);
        Build(safe).gate.BlockReason.Should().Be("safe mode (repeated unclean exits)");

        var nohide = new HostRuntimeOptions(CommandLineParser.Parse(new[] { "--no-hide-taskbar" }));
        Build(nohide).gate.BlockReason.Should().Be("--no-hide-taskbar");

        var normal = Build(Normal()).gate;
        normal.HidingBlocked.Should().BeFalse();
        normal.BlockReason.Should().BeNull();
    }
}

public class GuardedTaskbarTests
{
    private sealed class RecordingTaskbar : ITaskbarController
    {
        public TaskbarMode Current { get; private set; } = TaskbarMode.Visible;
        public List<TaskbarMode> Applied { get; } = new();
        public int Restores { get; private set; }
        public void Apply(TaskbarMode mode) { Applied.Add(mode); Current = mode; }
        public void Restore() => Restores++;
        public void Dispose() { }
    }

    [Fact]
    public void Hiding_is_ignored_when_blocked_but_showing_always_goes_through()
    {
        var inner = new RecordingTaskbar();
        var options = new HostRuntimeOptions(CommandLineParser.Parse(new[] { "--no-hide-taskbar" }));
        var gate = new ShellSafetyGate(options, new SurfaceRegistry(), new FakeHotkeyStatus(), new GuardianLauncher(NullLogger<GuardianLauncher>.Instance), NullLogger<ShellSafetyGate>.Instance);
        var guarded = new GuardedTaskbarController(inner, gate, NullLogger<GuardedTaskbarController>.Instance);

        guarded.Apply(TaskbarMode.Hidden);
        inner.Applied.Should().BeEmpty();
        guarded.Pending.Should().BeNull();

        guarded.Apply(TaskbarMode.Visible);
        inner.Applied.Should().Equal(TaskbarMode.Visible);
    }

    [Fact]
    public void A_hide_before_the_shell_is_ready_is_deferred_not_applied()
    {
        var inner = new RecordingTaskbar();
        var options = Normal();
        var gate = new ShellSafetyGate(options, new SurfaceRegistry(), new FakeHotkeyStatus(), new GuardianLauncher(NullLogger<GuardianLauncher>.Instance), NullLogger<ShellSafetyGate>.Instance);
        var guarded = new GuardedTaskbarController(inner, gate, NullLogger<GuardedTaskbarController>.Instance);

        guarded.Apply(TaskbarMode.Hidden);
        inner.Applied.Should().BeEmpty("dock and menu bar are not ready: the hide waits");
        guarded.Pending.Should().Be(TaskbarMode.Hidden);

        guarded.Apply(TaskbarMode.Visible);
        guarded.Pending.Should().BeNull("showing the taskbar cancels a deferred hide");
    }

    [Fact]
    public void Restore_always_reaches_the_inner_controller_and_drops_a_deferred_hide()
    {
        var inner = new RecordingTaskbar();
        var gate = new ShellSafetyGate(Normal(), new SurfaceRegistry(), new FakeHotkeyStatus(), new GuardianLauncher(NullLogger<GuardianLauncher>.Instance), NullLogger<ShellSafetyGate>.Instance);
        var guarded = new GuardedTaskbarController(inner, gate, NullLogger<GuardedTaskbarController>.Instance);
        guarded.Apply(TaskbarMode.Hidden);
        guarded.Restore();
        inner.Restores.Should().Be(1);
        guarded.Pending.Should().BeNull();
    }

    private static HostRuntimeOptions Normal() => new(CommandLineParser.Parse(Array.Empty<string>()));
}
