using System.Collections.Concurrent;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace BetterWindows.Native;

/// <summary>
/// Win32 implementation of <see cref="IWindowSystem"/>: enumeration/description of top-level windows, change notifications through
/// several NARROW SetWinEventHook registrations plus a shell-hook window (flash / activation), and the window commands.
/// Threading model: ONE dedicated STA thread (<c>BetterWindows.WindowHooks</c>) owns the hidden window, the hooks and the timers;
/// its callbacks only filter, throttle and enqueue. Events are raised by a second dedicated delivery thread, so a slow subscriber can
/// never stall the hook thread. Enumeration, description and commands are plain synchronous calls usable from any thread.
/// </summary>
public sealed unsafe partial class Win32WindowSystem : IWindowSystem
{
    private const int TimerThrottleFlush = 1;
    private const int TimerFullscreen = 2;
    private const int OutboxCapacity = 16384;

    private readonly ILogger _log;
    private readonly ProcessInfoCache _processes = new();
    private readonly object _gate = new();
    private readonly TrailingThrottle<(WindowEventKind Kind, long Handle)> _throttle;

    // Caches (all keyed by window handle unless noted).
    private readonly ConcurrentDictionary<long, byte> _topLevel = new();
    private readonly ConcurrentDictionary<long, byte> _flashing = new();
    private readonly ConcurrentDictionary<long, string> _titles = new();
    private readonly ConcurrentDictionary<long, (string? Aumid, long AtMs)> _windowAumids = new();
    private readonly ConcurrentDictionary<int, long> _titleBackoffUntilMs = new(); // by process id

    private MessageLoopThread? _loop;
    private BlockingCollection<WindowEvent>? _outbox;
    private Thread? _deliveryThread;
    private bool _disposed;

    /// <summary>Creates the window system. Nothing runs until <see cref="Start"/>.</summary>
    /// <param name="logger">Optional logger (warnings for failed hooks and OS calls).</param>
    public Win32WindowSystem(ILogger<Win32WindowSystem>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
        _throttle = new TrailingThrottle<(WindowEventKind, long)>(50, () => Environment.TickCount64);
    }

    /// <inheritdoc />
    public event Action<WindowEvent>? WindowChanged;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_loop != null || _disposed)
            {
                return;
            }

            _outbox = new BlockingCollection<WindowEvent>(OutboxCapacity);
            var outbox = _outbox;
            _deliveryThread = new Thread(() => DeliverLoop(outbox)) { IsBackground = true, Name = "BetterWindows.WindowEvents" };
            _deliveryThread.Start();

            var loop = new MessageLoopThread(
                "BetterWindows.WindowHooks",
                _log,
                messageOnly: false, // the shell-hook window must be a normal top-level window
                onStart: OnLoopStart,
                onStop: OnLoopStop,
                handler: OnLoopMessage);
            if (!loop.Start(TimeSpan.FromSeconds(5)))
            {
                _log.LogError("Window hook thread failed to start: window events are unavailable (enumeration and commands still work)");
                loop.Dispose();
                outbox.CompleteAdding();
                _outbox = null;
                return;
            }

            _loop = loop;
            _log.LogInformation("Window system started");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        MessageLoopThread? loop;
        BlockingCollection<WindowEvent>? outbox;
        Thread? delivery;
        lock (_gate)
        {
            loop = _loop;
            outbox = _outbox;
            delivery = _deliveryThread;
            _loop = null;
            _outbox = null;
            _deliveryThread = null;
        }

        loop?.Dispose(); // OnLoopStop runs on the hook thread: unhook, deregister, kill timers
        outbox?.CompleteAdding();
        if (delivery != null && delivery != Thread.CurrentThread && !delivery.Join(TimeSpan.FromSeconds(2)))
        {
            _log.LogWarning("Window event delivery thread did not finish in time");
        }

        _throttle.Clear();
        _topLevel.Clear();
        _flashing.Clear();
        if (loop != null)
        {
            _log.LogInformation("Window system stopped");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _processes.Dispose();
    }

    private void DeliverLoop(BlockingCollection<WindowEvent> outbox)
    {
        try
        {
            foreach (var e in outbox.GetConsumingEnumerable())
            {
                RaiseWindowChanged(e);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Window event delivery loop crashed");
        }
    }

    private void RaiseWindowChanged(WindowEvent e)
    {
        var handlers = WindowChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (var d in handlers.GetInvocationList())
        {
            try
            {
                ((Action<WindowEvent>)d)(e);
            }
            catch (Exception ex)
            {
                // A subscriber bug must never break the others or the loop.
                _log.LogWarning(ex, "WindowChanged subscriber threw for {Kind} {Handle}", e.Kind, e.Handle);
            }
        }
    }

    /// <summary>Enqueues an event for the delivery thread. Never blocks; drops (and logs once per burst) when the consumer is hopelessly behind.</summary>
    private void Emit(WindowEventKind kind, long handle)
    {
        var outbox = _outbox;
        if (outbox == null)
        {
            return;
        }

        try
        {
            if (!outbox.TryAdd(new WindowEvent(kind, handle)))
            {
                _log.LogWarning("Window event queue is full; dropping {Kind}", kind);
            }
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding raced with us during Stop.
        }
    }

    /// <summary>Emits immediately or defers to the trailing edge according to the 50 ms per-window limit.</summary>
    private void EmitThrottled(WindowEventKind kind, long handle)
    {
        if (_throttle.Offer((kind, handle)) == ThrottleDecision.Deliver)
        {
            Emit(kind, handle);
            return;
        }

        ArmTimer(TimerThrottleFlush, 50);
    }
}
