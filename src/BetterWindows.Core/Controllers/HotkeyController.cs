using BetterWindows.Core.Input;
using BetterWindows.Core.Overlay;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Global hotkeys. Registers settings.spotlight.hotkey (Spotlight), hotkeys.launchpad, hotkeys.missionControl, hotkeys.exit (quit and restore Windows) and
/// hotkeys.toggleTaskbar (temporary Hidden/Visible toggle of the native taskbar, not saved to settings) with <see cref="IHotkeyRegistrar"/>.
/// <list type="bullet">
/// <item>The first pass runs inside Start() (deterministic: the Host waits for an escape hotkey before hiding the taskbar); later settings changes are applied on the thread pool,
/// serialised and collapsed, and always converge to the newest settings.</item>
/// <item>A change unregisters every removed or changed hotkey FIRST, then registers the new ones, so swapping two gestures works. Unchanged hotkeys are left alone;
/// failed ones (taken by another app) are retried on every pass.</item>
/// <item>Every registration attempt and every unregistration is reported to <see cref="IHotkeyStatus"/> (id, canonical gesture, registered, error) for diagnostics and the settings UI. Duplicate
/// gestures are refused here (the earlier id in the priority order exit, toggleTaskbar, spotlight, launchpad, missionControl keeps it); unparsable ones report the reason.</item>
/// <item>Presses arrive on the registrar's worker thread: they are handed to the thread pool immediately.</item>
/// </list>
/// </summary>
public sealed class HotkeyController : IShellController
{
    private readonly ISettingsService _settings;
    private readonly IHotkeyRegistrar _registrar;
    private readonly IHotkeyStatus _status;
    private readonly ITaskbarController _taskbar;
    private readonly IAppLifecycle _lifecycle;
    private readonly ShellActions _actions;
    private readonly ILogger _log;
    private readonly SerialRunner _apply;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _active = new(StringComparer.Ordinal);   // id -> canonical gesture currently registered
    private readonly Dictionary<string, HotkeyRegistration> _reported = new(StringComparer.Ordinal);
    private Action<Settings>? _onSettings;
    private Action<string>? _onPressed;
    private bool _started, _stopped;

    public HotkeyController(ISettingsService settings, IHotkeyRegistrar registrar, IHotkeyStatus status, ITaskbarController taskbar, IAppLifecycle lifecycle,
        IServiceProvider services, ILogger<HotkeyController>? log = null)
    {
        _settings = settings;
        _registrar = registrar;
        _status = status;
        _taskbar = taskbar;
        _lifecycle = lifecycle;
        _actions = ShellActions.Create(services);
        _log = (ILogger?)log ?? NullLogger.Instance;
        _apply = new SerialRunner(ApplyLatest, _log);
    }

    public string Name => "HotkeyController";

    public void Start()
    {
        if (_started) return;
        _started = true;
        _onPressed = OnPressed;
        _registrar.Pressed += _onPressed;
        _apply.RunInline();
        _onSettings = _ => _apply.Trigger();
        _settings.Changed += _onSettings;
    }

    public void Stop()
    {
        lock (_gate) _stopped = true;
        if (_onSettings is not null) _settings.Changed -= _onSettings;
        if (_onPressed is not null) _registrar.Pressed -= _onPressed;
        _onSettings = null;
        _onPressed = null;
        _apply.Stop();

        string[] ids;
        lock (_gate) { ids = _active.Keys.ToArray(); _active.Clear(); }
        foreach (var id in ids) UnregisterQuietly(id);
        foreach (var id in HotkeyIds.All) _status.Clear(id);
        lock (_gate) _reported.Clear();
    }

    // ---- registration ----

    private enum Verdict { Unassigned, Invalid, Duplicate, Valid }

    private sealed record Plan(string Id, string Raw, Verdict Verdict, string Canonical, string? Error);

    /// <summary>Make the registrar match the CURRENT settings. Runs one at a time (SerialRunner).</summary>
    private void ApplyLatest()
    {
        lock (_gate) { if (_stopped) return; }
        var plans = BuildPlans(HotkeyBindings.From(_settings.Current));

        // 1. unregister everything removed or changed (before registering anything: gestures may move between ids)
        foreach (var plan in plans)
        {
            string? registered;
            lock (_gate) registered = _active.GetValueOrDefault(plan.Id);
            if (registered is null) continue;
            if (plan.Verdict == Verdict.Valid && plan.Canonical == registered) continue;
            lock (_gate) _active.Remove(plan.Id);
            UnregisterQuietly(plan.Id);
        }

        // 2. register / report
        foreach (var plan in plans)
        {
            lock (_gate) { if (_stopped) return; }
            switch (plan.Verdict)
            {
                case Verdict.Unassigned:
                    Forget(plan.Id);
                    break;
                case Verdict.Invalid:
                case Verdict.Duplicate:
                    Report(new HotkeyRegistration(plan.Id, plan.Raw, false, plan.Error));
                    break;
                case Verdict.Valid:
                    bool already;
                    lock (_gate) already = _active.ContainsKey(plan.Id);
                    if (already) break;
                    Register(plan);
                    break;
            }
        }
    }

    private List<Plan> BuildPlans(IReadOnlyList<KeyValuePair<string, string>> bindings)
    {
        var plans = new List<Plan>(bindings.Count);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal); // canonical gesture -> id that keeps it
        foreach (var (id, rawValue) in bindings)
        {
            var raw = (rawValue ?? "").Trim();
            if (raw.Length == 0) { plans.Add(new Plan(id, "", Verdict.Unassigned, "", null)); continue; }
            var shown = InputPolicy.Truncate(raw, HotkeyParser.MaxLength);
            if (!HotkeyParser.TryParse(raw, out var gesture) || !HotkeyParser.TryFormat(gesture, out var canonical))
            {
                plans.Add(new Plan(id, shown, Verdict.Invalid, "", "not a valid hotkey"));
                continue;
            }
            if (owners.TryGetValue(canonical, out var owner))
            {
                plans.Add(new Plan(id, canonical, Verdict.Duplicate, canonical, $"already assigned to '{owner}'"));
                continue;
            }
            owners[canonical] = id;
            plans.Add(new Plan(id, canonical, Verdict.Valid, canonical, null));
        }
        return plans;
    }

    private void Register(Plan plan)
    {
        bool ok;
        string? error = null;
        try
        {
            HotkeyParser.TryParse(plan.Canonical, out var gesture);
            ok = _registrar.TryRegister(plan.Id, gesture);
            if (!ok) error = "already in use by another application";
        }
        catch (Exception ex)
        {
            ok = false;
            error = "registration failed";
            _log.LogWarning(ex, "Registering hotkey {Id} ({Gesture}) threw", plan.Id, plan.Canonical);
        }

        if (ok) lock (_gate) _active[plan.Id] = plan.Canonical;
        else _log.LogWarning("Hotkey {Id} ({Gesture}) could not be registered: {Error}", plan.Id, plan.Canonical, error);
        Report(new HotkeyRegistration(plan.Id, plan.Canonical, ok, error));
    }

    private void UnregisterQuietly(string id)
    {
        try { _registrar.Unregister(id); }
        catch (Exception ex) { _log.LogDebug(ex, "Unregistering hotkey {Id} failed", id); }
    }

    /// <summary>Reports to IHotkeyStatus only when the outcome for an id actually changed (retries of a taken hotkey must not spam listeners).</summary>
    private void Report(HotkeyRegistration registration)
    {
        lock (_gate)
        {
            if (_reported.TryGetValue(registration.Id, out var last) && last == registration) return;
            _reported[registration.Id] = registration;
        }
        _status.Report(registration);
    }

    private void Forget(string id)
    {
        bool had;
        lock (_gate) had = _reported.Remove(id);
        _status.Clear(id);
        if (had) _log.LogInformation("Hotkey {Id} is now unassigned", id);
    }

    // ---- presses (registrar worker thread) ----

    private void OnPressed(string id)
    {
        _ = Task.Run(() =>
        {
            try { Dispatch(id); }
            catch (Exception ex) { _log.LogWarning(ex, "Hotkey {Id} handler failed", id); }
        });
    }

    private void Dispatch(string id)
    {
        switch (id)
        {
            case HotkeyIds.Spotlight: _actions.Execute(ShellAction.Spotlight); break;
            case HotkeyIds.Launchpad: _actions.Execute(ShellAction.Launchpad); break;
            case HotkeyIds.MissionControl: _actions.Execute(ShellAction.MissionControl); break;
            case HotkeyIds.Exit: _lifecycle.Exit(); break;
            case HotkeyIds.ToggleTaskbar: ToggleTaskbar(); break;
        }
    }

    /// <summary>
    /// Temporary toggle (settings are not written): Visible -> the configured mode (Hidden if the configured mode is Visible too), anything else -> Visible.
    /// The native taskbar can therefore always be summoned with this hotkey, and the next press puts things back.
    /// </summary>
    private void ToggleTaskbar()
    {
        var current = _taskbar.Current;
        TaskbarMode target;
        if (current != TaskbarMode.Visible) target = TaskbarMode.Visible;
        else
        {
            var configured = _settings.Current.Taskbar.Mode;
            target = configured == TaskbarMode.Visible ? TaskbarMode.Hidden : configured;
        }
        _taskbar.Apply(target);
    }
}
