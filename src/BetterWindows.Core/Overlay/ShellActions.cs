using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Overlay;

/// <summary>
/// Executes the shell actions a hotkey or a hot corner can trigger. Overlay modes (Spotlight, Launchpad, Mission Control) TOGGLE: visible in the same mode -> hide, otherwise show
/// that mode on the monitor under the cursor (or the one the caller names). ShowDesktop goes through <see cref="IInputSimulator"/>; LockScreen and Sleep through <see cref="ISystemPower"/>;
/// an open overlay is hidden first so it does not cover (or survive) the action. Only enum values are accepted, never strings: nothing from the UI or from a settings file
/// can be turned into a command. Never throws (it runs on timer and hotkey threads): failures are logged and reported as <c>false</c>.
/// </summary>
public sealed class ShellActions
{
    private readonly OverlayCoordinator _overlay;
    private readonly IInputSimulator _input;
    private readonly ISystemPower _power;
    private readonly ILogger _log;

    public ShellActions(OverlayCoordinator overlay, IInputSimulator input, ISystemPower power, ILogger<ShellActions>? log = null)
    {
        _overlay = overlay;
        _input = input;
        _power = power;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>
    /// The instance shared through DI when the composition root registers one, otherwise a new one over the registered singletons (the instances are interchangeable: the
    /// state lives in <see cref="ISurfaceManager"/> and <see cref="Services.OverlayStateService"/>).
    /// </summary>
    public static ShellActions Create(IServiceProvider services)
    {
        var registered = services.GetService<ShellActions>();
        if (registered is not null) return registered;
        var overlay = services.GetService<OverlayCoordinator>() ?? ActivatorUtilities.CreateInstance<OverlayCoordinator>(services);
        return new ShellActions(overlay, services.GetRequiredService<IInputSimulator>(), services.GetRequiredService<ISystemPower>(), services.GetService<ILogger<ShellActions>>());
    }

    /// <summary>Hot-corner entry point. <see cref="HotCornerAction.None"/> does nothing. Returns true if an action was executed.</summary>
    public bool Execute(HotCornerAction action, string? monitorId = null) => action switch
    {
        HotCornerAction.MissionControl => Execute(ShellAction.MissionControl, monitorId),
        HotCornerAction.Launchpad => Execute(ShellAction.Launchpad, monitorId),
        HotCornerAction.Spotlight => Execute(ShellAction.Spotlight, monitorId),
        HotCornerAction.ShowDesktop => Execute(ShellAction.ShowDesktop, monitorId),
        HotCornerAction.LockScreen => Execute(ShellAction.LockScreen, monitorId),
        HotCornerAction.Sleep => Execute(ShellAction.Sleep, monitorId),
        _ => false,
    };

    /// <summary>Hotkey entry point. Returns true if the action ran without failing.</summary>
    public bool Execute(ShellAction action, string? monitorId = null)
    {
        try
        {
            switch (action)
            {
                case ShellAction.Spotlight: _overlay.Toggle(OverlayMode.Spotlight, monitorId); return true;
                case ShellAction.Launchpad: _overlay.Toggle(OverlayMode.Launchpad, monitorId); return true;
                case ShellAction.MissionControl: _overlay.Toggle(OverlayMode.MissionControl, monitorId); return true;
                case ShellAction.ShowDesktop: HideOverlayIfShown(); _input.ShowDesktop(); return true;
                case ShellAction.LockScreen: HideOverlayIfShown(); _power.Perform(PowerAction.Lock); return true;
                case ShellAction.Sleep: HideOverlayIfShown(); _power.Perform(PowerAction.Sleep); return true;
                default: return false;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shell action {Action} failed", action);
            return false;
        }
    }

    private void HideOverlayIfShown()
    {
        if (_overlay.IsVisible) _overlay.Hide();
    }
}
