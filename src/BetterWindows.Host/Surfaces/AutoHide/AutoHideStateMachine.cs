namespace BetterWindows.Host.Surfaces;

/// <summary>Timing of the auto-hide state machine (docs: reveal after 150 ms at the edge, hide after 400 ms away).</summary>
internal sealed record AutoHideOptions(TimeSpan RevealDelay, TimeSpan HideDelay)
{
    public static readonly AutoHideOptions Default = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400));
}

internal enum AutoHideTransition { None, Reveal, Hide }

/// <summary>
/// Pure auto-hide state machine for the dock (and the auto-hiding menu bar). The caller samples the pointer every ~50 ms and passes monotonic time plus two booleans
/// computed with <see cref="AutoHideZones"/>:
///  - <c>inRevealZone</c>: the pointer is in the thin edge strip (hidden -> revealed after <see cref="AutoHideOptions.RevealDelay"/> of CONTINUOUS presence);
///  - <c>inKeepZone</c>: the pointer is on the dock rect + margin (revealed -> hidden only after <see cref="AutoHideOptions.HideDelay"/> of CONTINUOUS absence from both zones).
/// Hiding is additionally blocked while an overlay is visible (a dockMenu popover is open) and while Core forces the dock revealed (ISurfaceManager.SetDockRevealed).
/// </summary>
internal sealed class AutoHideStateMachine
{
    private readonly AutoHideOptions _options;
    private TimeSpan? _enteredAt;
    private TimeSpan? _leftAt;

    public AutoHideStateMachine(AutoHideOptions? options = null, bool initiallyRevealed = false)
    {
        _options = options ?? AutoHideOptions.Default;
        Revealed = initiallyRevealed;
    }

    /// <summary>Whether the surface is currently revealed.</summary>
    public bool Revealed { get; private set; }

    /// <summary>Core override: keep revealed regardless of the pointer.</summary>
    public bool Forced { get; private set; }

    /// <summary>One pointer sample. Returns the transition it caused (the caller publishes dock.reveal and animates the window).</summary>
    public AutoHideTransition Update(TimeSpan now, bool inRevealZone, bool inKeepZone, bool overlayVisible)
    {
        if (!Revealed)
        {
            _leftAt = null;
            if (!inRevealZone) { _enteredAt = null; return AutoHideTransition.None; }
            _enteredAt ??= now;
            if (now - _enteredAt.Value < _options.RevealDelay) return AutoHideTransition.None;
            return Reveal();
        }

        _enteredAt = null;
        if (inKeepZone || inRevealZone || overlayVisible || Forced) { _leftAt = null; return AutoHideTransition.None; }
        _leftAt ??= now;
        if (now - _leftAt.Value < _options.HideDelay) return AutoHideTransition.None;
        Revealed = false;
        _leftAt = null;
        return AutoHideTransition.Hide;
    }

    /// <summary>Core override on/off. Forcing a hidden surface reveals it immediately; releasing restarts the hide countdown from the next sample.</summary>
    public AutoHideTransition SetForced(bool forced)
    {
        if (Forced == forced) return AutoHideTransition.None;
        Forced = forced;
        if (forced && !Revealed) return Reveal();
        _leftAt = null;
        return AutoHideTransition.None;
    }

    /// <summary>Re-initialises (auto-hide was switched on/off or the surface was re-created). Forced is kept: it belongs to Core.</summary>
    public void Reset(bool revealed)
    {
        Revealed = revealed;
        _enteredAt = null;
        _leftAt = null;
    }

    private AutoHideTransition Reveal()
    {
        Revealed = true;
        _enteredAt = null;
        _leftAt = null;
        return AutoHideTransition.Reveal;
    }
}
