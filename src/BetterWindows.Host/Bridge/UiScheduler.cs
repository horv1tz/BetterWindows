using System.Windows.Threading;

namespace BetterWindows.Host.Bridge;

/// <summary>Runs work on the UI thread. Abstracted so the channel/queue logic is testable without a WPF dispatcher.</summary>
public interface IUiScheduler
{
    /// <summary>True when the caller already is the UI thread.</summary>
    bool CheckAccess();
    /// <summary>Queues <paramref name="action"/> on the UI thread and returns immediately. Exceptions are the action's business (callers wrap their own try/catch).</summary>
    void Post(Action action);
}

/// <summary><see cref="IUiScheduler"/> over the WPF <see cref="Dispatcher"/>.</summary>
public sealed class DispatcherScheduler : IUiScheduler
{
    private readonly Dispatcher _dispatcher;
    public DispatcherScheduler(Dispatcher dispatcher) { _dispatcher = dispatcher; }
    public bool CheckAccess() => _dispatcher.CheckAccess();
    public void Post(Action action) => _dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
}
