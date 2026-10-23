using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BetterWindows.Core.Platform;
using BetterWindows.Host.Surfaces;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Tray;

/// <summary>
/// The notification-area icon of the shell: opens Settings and quits. Created and disposed on the UI thread; its menu only calls ports that are themselves
/// thread-safe (<see cref="ISurfaceManager.ShowSettingsWindow"/>, <see cref="IAppLifecycle.Exit"/>). Labels follow the Windows UI language (English / Russian).
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly ISurfaceManager _surfaces;
    private readonly IAppLifecycle _app;
    private readonly ILogger<TrayIconHost> _log;
    private TaskbarIcon? _icon;

    public TrayIconHost(ISurfaceManager surfaces, IAppLifecycle app, ILogger<TrayIconHost> log)
    {
        _surfaces = surfaces;
        _app = app;
        _log = log;
    }

    /// <summary>Adds the icon (UI thread). Failure is logged: the shell works without a tray icon, the user can still quit with the exit hotkey.</summary>
    public void Start()
    {
        if (_icon is not null) return;
        try
        {
            var ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
            var menu = new ContextMenu();
            var settings = new MenuItem { Header = ru ? "Настройки…" : "Settings…" };
            settings.Click += (_, _) => Invoke(() => _surfaces.ShowSettingsWindow(null));
            var quit = new MenuItem { Header = ru ? "Выйти из BetterWindows" : "Quit BetterWindows" };
            quit.Click += (_, _) => Invoke(() => _app.Exit());
            menu.Items.Add(settings);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);

            var icon = new TaskbarIcon
            {
                ToolTipText = "BetterWindows",
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenu = menu,
            };
            icon.DoubleClickCommand = new RelayCommand(() => Invoke(() => _surfaces.ShowSettingsWindow(null)));
            icon.ForceCreate(false); // enablesEfficiencyMode: false, the icon must stay responsive while the shell runs
            _icon = icon;
        }
        catch (Exception ex) { _log.LogWarning(ex, "The tray icon could not be created"); }
    }

    private void Invoke(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.LogError(ex, "Tray action failed"); }
    }

    public void Dispose()
    {
        var icon = _icon;
        _icon = null;
        try { icon?.Dispose(); }
        catch (Exception ex) { _log.LogDebug(ex, "Disposing the tray icon failed"); }
    }

    /// <summary>Minimal ICommand for <see cref="TaskbarIcon.DoubleClickCommand"/>.</summary>
    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) { _action = action; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
    }
}
