using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Hosting;

/// <summary>Starts/stops all <see cref="IShellController"/>s with per-controller failure isolation.</summary>
public sealed class ShellRuntime
{
    private readonly IReadOnlyList<IShellController> _controllers;
    private readonly ILogger<ShellRuntime> _log;
    private readonly List<IShellController> _started = new();

    public ShellRuntime(IEnumerable<IShellController> controllers, ILogger<ShellRuntime> log)
    {
        _controllers = controllers.ToArray();
        _log = log;
    }

    public IReadOnlyList<IShellController> Started => _started;

    public void Start()
    {
        foreach (var c in _controllers)
        {
            try
            {
                c.Start();
                _started.Add(c);
                _log.LogInformation("Controller {Name} started", c.Name);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Controller {Name} failed to start; continuing without it", c.Name);
            }
        }
    }

    public void Stop()
    {
        for (var i = _started.Count - 1; i >= 0; i--)
        {
            try { _started[i].Stop(); }
            catch (Exception ex) { _log.LogError(ex, "Controller {Name} failed to stop", _started[i].Name); }
        }
        _started.Clear();
    }
}
