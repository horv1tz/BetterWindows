using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Decorator over the Win32 <see cref="ITaskbarController"/> enforcing <see cref="ShellSafetyGate"/>: hiding is ignored when blocked, DEFERRED until the shell is ready to
/// hide (then applied automatically), and always preceded by starting the guardian. Showing the taskbar is never delayed or blocked. Everything else is passed through.
/// </summary>
public sealed class GuardedTaskbarController : ITaskbarController
{
    private readonly ITaskbarController _inner;
    private readonly ShellSafetyGate _gate;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private TaskbarMode? _pending;
    private bool _loggedBlock;

    public GuardedTaskbarController(ITaskbarController inner, ShellSafetyGate gate, ILogger<GuardedTaskbarController> log)
    {
        _inner = inner;
        _gate = gate;
        _log = log;
        _gate.ReadyChanged += OnReadyChanged;
    }

    /// <summary>The real state of the native taskbar (a deferred or blocked request is not reflected here).</summary>
    public TaskbarMode Current => _inner.Current;
    /// <summary>The mode waiting for the shell to become ready (null = none). Diagnostics.</summary>
    public TaskbarMode? Pending { get { lock (_lock) return _pending; } }

    public void Apply(TaskbarMode mode)
    {
        if (mode == TaskbarMode.Visible)
        {
            lock (_lock) _pending = null;
            _inner.Apply(mode);
            return;
        }
        if (_gate.HidingBlocked)
        {
            if (!_loggedBlock) { _loggedBlock = true; _log.LogInformation("Taskbar mode {Mode} ignored: {Reason}", mode, _gate.BlockReason); }
            return;
        }
        if (!_gate.IsReadyToHide(out var reason))
        {
            lock (_lock) _pending = mode;
            _log.LogInformation("Taskbar mode {Mode} deferred: {Reason}", mode, reason);
            return;
        }
        lock (_lock) _pending = null;
        _gate.EnsureGuardian();
        _inner.Apply(mode);
    }

    public void Restore()
    {
        lock (_lock) _pending = null;
        _inner.Restore();
    }

    private void OnReadyChanged()
    {
        TaskbarMode? mode;
        lock (_lock) mode = _pending;
        if (mode is null || !_gate.IsReadyToHide(out _)) return;
        lock (_lock)
        {
            if (_pending != mode) return;
            _pending = null;
        }
        try
        {
            _log.LogInformation("Applying deferred taskbar mode {Mode}", mode);
            _gate.EnsureGuardian();
            _inner.Apply(mode.Value);
        }
        catch (Exception ex) { _log.LogError(ex, "Applying the deferred taskbar mode failed"); }
    }

    public void Dispose()
    {
        _gate.ReadyChanged -= OnReadyChanged;
        _inner.Dispose();
    }
}

/// <summary>Decorator over <see cref="IDesktopIconsController"/>: no desktop layer in safe mode, no hiding of Explorer's icons when blocked, guardian before the first change.</summary>
public sealed class GuardedDesktopIconsController : IDesktopIconsController
{
    private readonly IDesktopIconsController _inner;
    private readonly ShellSafetyGate _gate;
    private readonly ILogger _log;

    public GuardedDesktopIconsController(IDesktopIconsController inner, ShellSafetyGate gate, ILogger<GuardedDesktopIconsController> log)
    {
        _inner = inner;
        _gate = gate;
        _log = log;
    }

    public void SetNativeIconsVisible(bool visible)
    {
        if (visible) { _inner.SetNativeIconsVisible(true); return; }
        if (_gate.HidingBlocked) { _log.LogInformation("Hiding Explorer's desktop icons ignored: {Reason}", _gate.BlockReason); return; }
        _gate.EnsureGuardian();
        _inner.SetNativeIconsVisible(false);
    }

    public bool TryAttachDesktopLayer(nint hwnd, NativeRect boundsPx)
    {
        if (_gate.Options.DesktopLayerDisabled) { _log.LogInformation("Desktop layer disabled in safe mode"); return false; }
        return _inner.TryAttachDesktopLayer(hwnd, boundsPx);
    }

    public void DetachDesktopLayer(nint hwnd) => _inner.DetachDesktopLayer(hwnd);

    public event Action? ShellRestarted { add => _inner.ShellRestarted += value; remove => _inner.ShellRestarted -= value; }

    public void Restore() => _inner.Restore();
    public void Dispose() => _inner.Dispose();
}

/// <summary>Decorator over <see cref="IHotkeyRegistrar"/>: safe mode registers no global hotkeys.</summary>
public sealed class GuardedHotkeyRegistrar : IHotkeyRegistrar
{
    private readonly IHotkeyRegistrar _inner;
    private readonly HostRuntimeOptions _options;
    private readonly ILogger _log;

    public GuardedHotkeyRegistrar(IHotkeyRegistrar inner, HostRuntimeOptions options, ILogger<GuardedHotkeyRegistrar> log)
    {
        _inner = inner;
        _options = options;
        _log = log;
    }

    public bool TryRegister(string id, HotkeyGesture gesture)
    {
        if (_options.HotkeysDisabled) { _log.LogInformation("Hotkey {Id} not registered: safe mode", id); return false; }
        return _inner.TryRegister(id, gesture);
    }

    public void Unregister(string id) => _inner.Unregister(id);
    public void UnregisterAll() => _inner.UnregisterAll();

    public event Action<string>? Pressed { add => _inner.Pressed += value; remove => _inner.Pressed -= value; }

    public void Dispose() => _inner.Dispose();
}
