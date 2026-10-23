namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Owner: surfaces stream. Creates/destroys the dock, menu bar(s), overlay and desktop windows according to settings and
/// monitor changes; handles auto-hide, fullscreen suppression and AppBar reservation. Started by the lifecycle stream
/// after ShellRuntime.Start(); Stop() must release every AppBar reservation.
/// </summary>
public interface ISurfaceHost
{
    void Start();
    void Stop();
}
