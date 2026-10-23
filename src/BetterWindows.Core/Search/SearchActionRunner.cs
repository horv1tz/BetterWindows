using System.Text.Json.Nodes;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Search;

/// <summary>
/// Executes the operation of a chosen search result. Every target is re-validated here (InputPolicy, catalog ids, tracked windows with the process id they
/// had, the ms-settings / http(s) allow-lists), so nothing the UI sends reaches the shell unchecked. Throws <see cref="ProtocolException"/> on refusal or failure.
/// </summary>
public sealed class SearchActionRunner
{
    private readonly IAppCatalog _catalog;
    private readonly IAppLauncher _launcher;
    private readonly IClipboard _clipboard;
    private readonly ISystemPower _power;
    private readonly IAppLifecycle _lifecycle;
    private readonly ITaskbarController _taskbar;
    private readonly IHotkeyStatus _hotkeys;
    private readonly IInputSimulator _input;
    private readonly ISurfaceManager _surfaces;
    private readonly ISettingsService _settings;
    private readonly IWindowTracker _tracker;
    private readonly IWindowSystem _windows;
    private readonly UsageStore _usage;
    private readonly ILogger _log;

    public SearchActionRunner(
        IAppCatalog catalog, IAppLauncher launcher, IClipboard clipboard, ISystemPower power, IAppLifecycle lifecycle, ITaskbarController taskbar,
        IHotkeyStatus hotkeys, IInputSimulator input, ISurfaceManager surfaces, ISettingsService settings, IWindowTracker tracker, IWindowSystem windows,
        UsageStore usage, ILogger<SearchActionRunner>? log = null)
    {
        _catalog = catalog;
        _launcher = launcher;
        _clipboard = clipboard;
        _power = power;
        _lifecycle = lifecycle;
        _taskbar = taskbar;
        _hotkeys = hotkeys;
        _input = input;
        _surfaces = surfaces;
        _settings = settings;
        _tracker = tracker;
        _windows = windows;
        _usage = usage;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public async Task RunAsync(SearchOperation operation, CancellationToken ct)
    {
        switch (operation)
        {
            case LaunchAppOperation launch: Launch(launch.AppId); break;
            case OpenUriOperation uri: OpenUri(uri.Uri); break;
            case OpenPathOperation path: OpenPath(path.Path); break;
            case RevealPathOperation reveal: RevealPath(reveal.Path); break;
            case CopyTextOperation copy: Copy(copy.Text); break;
            case PowerOperation power: _power.Perform(power.Action); break;
            case ThemeOperation theme:
                // Same write as theme.setPreference: the app preference only, never the Windows theme (docs/IPC.md, status stream).
                await _settings.PatchAsync(new JsonObject
                {
                    ["appearance"] = new JsonObject { ["mode"] = ThemeWire(theme.Preference) },
                }, ct).ConfigureAwait(false);
                break;
            case TaskbarOperation taskbar: SetTaskbar(taskbar.Mode); break;
            case ShowDesktopOperation: _input.ShowDesktop(); break;
            case ShowSettingsOperation settings: _surfaces.ShowSettingsWindow(settings.Page); break;
            case QuitOperation: _lifecycle.Exit(); break;
            case ActivateWindowOperation window: Activate(window); break;
            default: throw ProtocolException.BadRequest("unsupported search operation");
        }
    }

    private void Launch(string appId)
    {
        var entry = _catalog.FindById(appId) ?? throw ProtocolException.NotFound("the app could not be found any more");
        LaunchTarget target;
        if (entry.Aumid is { Length: > 0 } aumid) target = LaunchTarget.FromAumid(aumid);
        else if (entry.Path is not null && InputPolicy.TryNormalizeLocalPath(entry.Path, out var path)) target = LaunchTarget.FromPath(path);
        else throw ProtocolException.NotFound("the app has no launchable target");

        switch (_launcher.Launch(target))
        {
            case LaunchResult.Started:
                _usage.RecordLaunch(entry.Id);
                return;
            case LaunchResult.NotFound:
                throw ProtocolException.NotFound("the app could not be found any more");
            default:
                throw ProtocolException.Failed("the app could not be started");
        }
    }

    /// <summary>Only ms-settings URIs and http(s) URLs; anything else is refused before the launcher sees it.</summary>
    private void OpenUri(string uri)
    {
        bool allowed = InputPolicy.IsMsSettingsUri(uri) || InputPolicy.IsSafeWebUrl(uri, out _);
        if (!allowed) throw ProtocolException.BadRequest("only settings pages and web addresses can be opened");
        if (!_launcher.Open(uri)) throw ProtocolException.Failed("the address could not be opened");
    }

    private void OpenPath(string path)
    {
        if (!InputPolicy.TryNormalizeLocalPath(path, out var normalized)) throw ProtocolException.BadRequest("invalid path");
        if (!_launcher.Open(normalized)) throw ProtocolException.Failed("the item could not be opened");
    }

    private void RevealPath(string path)
    {
        if (!InputPolicy.TryNormalizeLocalPath(path, out var normalized)) throw ProtocolException.BadRequest("invalid path");
        if (!_launcher.RevealInExplorer(normalized)) throw ProtocolException.Failed("Explorer could not show the item");
    }

    private void Copy(string text)
    {
        _clipboard.SetText(InputPolicy.Truncate(text, 8192));
    }

    /// <summary>Hiding the taskbar is only allowed while an escape hotkey is registered (the same rule SystemController applies).</summary>
    private void SetTaskbar(TaskbarMode mode)
    {
        if (mode == TaskbarMode.Hidden && !_hotkeys.EscapeHotkeyRegistered)
        {
            throw ProtocolException.Unsupported("the taskbar can be hidden only while the exit or toggle-taskbar shortcut is registered");
        }
        _taskbar.Apply(mode);
    }

    /// <summary>Restores a minimised window and brings it forward. The handle must still be a tracked window of the same process.</summary>
    private void Activate(ActivateWindowOperation op)
    {
        var tracked = _tracker.Find(op.Handle) ?? throw ProtocolException.NotFound("the window is gone");
        var raw = _windows.TryDescribe(op.Handle) ?? throw ProtocolException.NotFound("the window is gone");
        if (raw.ProcessId != tracked.Raw.ProcessId || tracked.Raw.ProcessId != op.ProcessId)
        {
            throw ProtocolException.NotFound("the window changed");
        }
        if (raw.IsMinimized) _windows.Restore(op.Handle);
        if (!_windows.Activate(op.Handle))
        {
            _log.LogInformation("could not activate window {Handle}", op.Handle);
            throw ProtocolException.Failed("the window could not be activated");
        }
    }

    private static string ThemeWire(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => "auto",
    };
}
