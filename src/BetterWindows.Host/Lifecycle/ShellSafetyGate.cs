using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Host-side safety net around the operations that change the user's shell (docs/IPC.md section 15, rules 5 and 9). Core's SystemController decides WHEN to hide the taskbar;
/// the gate guarantees it can never happen (a) in smoke / safe mode / <c>--no-hide-taskbar</c>, (b) before the dock and the menu bar completed <c>surface.ready</c> and an
/// escape hotkey (<c>exit</c> / <c>toggleTaskbar</c>) is registered, (c) without a guardian process watching this one. Used by the guarded port decorators.
/// </summary>
public sealed class ShellSafetyGate : IDisposable
{
    private readonly HostRuntimeOptions _options;
    private readonly SurfaceRegistry _registry;
    private readonly IHotkeyStatus _hotkeys;
    private readonly GuardianLauncher _guardian;
    private readonly ILogger<ShellSafetyGate> _log;

    public ShellSafetyGate(HostRuntimeOptions options, SurfaceRegistry registry, IHotkeyStatus hotkeys, GuardianLauncher guardian, ILogger<ShellSafetyGate> log)
    {
        _options = options;
        _registry = registry;
        _hotkeys = hotkeys;
        _guardian = guardian;
        _log = log;
        _registry.SurfaceReady += OnSurfaceReady;
        _hotkeys.Changed += OnHotkeysChanged;
    }

    /// <summary>Raised (on any thread) when something that <see cref="IsReadyToHide"/> depends on changed.</summary>
    public event Action? ReadyChanged;

    /// <summary>The native taskbar must stay visible for the whole run (smoke, safe mode, <c>--no-hide-taskbar</c>).</summary>
    public bool HidingBlocked => _options.NoHideTaskbar;
    /// <summary>Why hiding is blocked, for logs and diag.report.</summary>
    public string? BlockReason => !_options.NoHideTaskbar ? null : _options.Smoke ? "smoke mode" : _options.SafeMode ? $"safe mode ({_options.SafeModeReason})" : "--no-hide-taskbar";
    public HostRuntimeOptions Options => _options;

    /// <summary>Dock and menu bar completed surface.ready AND an escape hotkey is registered.</summary>
    public bool IsReadyToHide(out string reason)
    {
        if (!_registry.OfKind(SurfaceKind.Dock).Any(c => _registry.IsReady(c))) { reason = "the dock has not completed surface.ready"; return false; }
        if (!_registry.OfKind(SurfaceKind.Menubar).Any(c => _registry.IsReady(c))) { reason = "the menu bar has not completed surface.ready"; return false; }
        if (!_hotkeys.EscapeHotkeyRegistered) { reason = "no escape hotkey (exit / toggleTaskbar) is registered"; return false; }
        reason = "";
        return true;
    }

    /// <summary>Starts the guardian if it is not running yet. Called right before the first mutation of the shell.</summary>
    public void EnsureGuardian()
    {
        if (!_guardian.EnsureStarted()) _log.LogWarning("Continuing without a guardian process; a crash will be repaired by the next start");
    }

    private void OnSurfaceReady(SurfaceId _) => ReadyChanged?.Invoke();
    private void OnHotkeysChanged() => ReadyChanged?.Invoke();

    public void Dispose()
    {
        _registry.SurfaceReady -= OnSurfaceReady;
        _hotkeys.Changed -= OnHotkeysChanged;
    }
}
