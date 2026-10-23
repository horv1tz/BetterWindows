using System.Windows;

namespace BetterWindows.Host;

/// <summary>STUB — owner: lifecycle stream. Real startup: single instance, crash-restore, DI composition, ShellRuntime, SurfaceHost, tray icon.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Shutdown();
    }
}
