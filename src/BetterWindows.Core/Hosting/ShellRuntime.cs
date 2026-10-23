using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Hosting;

/// <summary>
/// Starts/stops Core services and controllers with per-item failure isolation. Services start first (declaration order), controllers after; stop is the exact reverse.
/// A controller whose Start throws is stopped best-effort (partial registrations/subscriptions are released) and skipped; a controller that could not even be
/// constructed shows up as a <see cref="FailedController"/> and is reported the same way.
/// </summary>
public sealed class ShellRuntime
{
    private readonly IReadOnlyList<IShellService> _services;
    private readonly IReadOnlyList<IShellController> _controllers;
    private readonly ILogger<ShellRuntime> _log;
    private readonly List<IShellController> _started = new();
    private readonly List<IShellService> _startedServices = new();

    public ShellRuntime(IEnumerable<IShellService> services, IEnumerable<IShellController> controllers, ILogger<ShellRuntime> log)
    {
        _services = services.ToArray();
        _controllers = controllers.ToArray();
        _log = log;
    }

    public IReadOnlyList<IShellController> Started => _started;
    public IReadOnlyList<IShellService> StartedServices => _startedServices;

    public void Start()
    {
        foreach (var s in _services)
        {
            try { s.Start(); _startedServices.Add(s); _log.LogInformation("Service {Name} started", s.Name); }
            catch (Exception ex) { _log.LogError(ex, "Service {Name} failed to start; continuing without it", s.Name); TryStop(s); }
        }
        foreach (var c in _controllers)
        {
            try { c.Start(); _started.Add(c); _log.LogInformation("Controller {Name} started", c.Name); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Controller {Name} failed to start; continuing without it", c.Name);
                try { c.Stop(); } catch (Exception stopEx) { _log.LogDebug(stopEx, "Cleanup stop of failed controller {Name} also failed", c.Name); }
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
        for (var i = _startedServices.Count - 1; i >= 0; i--) TryStop(_startedServices[i]);
        _startedServices.Clear();
    }

    private void TryStop(IShellService s)
    {
        try { s.Stop(); } catch (Exception ex) { _log.LogError(ex, "Service {Name} failed to stop", s.Name); }
    }
}

/// <summary>Stand-in for a controller whose constructor threw (usually a platform port that could not be created): Start() reports it.</summary>
public sealed class FailedController : IShellController
{
    private readonly Exception _cause;
    public FailedController(string name, Exception cause) { Name = name; _cause = cause; }
    public string Name { get; }
    public void Start() => throw new InvalidOperationException($"{Name}: construction failed", _cause);
    public void Stop() { }
}
