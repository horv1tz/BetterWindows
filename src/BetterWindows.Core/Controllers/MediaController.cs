using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// "Now playing": publishes media.state (state event on <see cref="IStatePublisher"/>) from <see cref="IMediaSource"/> and executes media.control.
/// <para>thumbUrl is built here (<see cref="DynUrls.MediaArt"/>); its revision increments ONLY when title, artist, album or the cover art changes, so the UI re-fetches art exactly then.
/// Art "exists" when the source marks it in <c>Current.ThumbUrl</c> (any non-empty value; a new value means new art); a source that leaves it empty is probed once per track
/// through <see cref="IMediaSource.GetArtAsync"/> (a few bounded retries, because players often deliver art after the title).</para>
/// <para>positionSec is a snapshot taken when the state is published and the UI extrapolates while playing. To let it resync the controller re-publishes at most every 5 s,
/// but only while something is playing, a menubar/overlay/desktop is ready to show it, and the source position drifted more than 2 s from what the UI extrapolates.</para>
/// </summary>
public sealed class MediaController : IShellController
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);
    /// <summary>Drift check period; also the upper bound on how often the position is re-published.</summary>
    internal static readonly TimeSpan DriftPeriod = TimeSpan.FromSeconds(5);
    internal const double DriftThresholdSec = 2.0;
    private static readonly TimeSpan ArtProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ArtProbeRetryGap = TimeSpan.FromSeconds(1);
    private const int MaxArtProbesPerTrack = 4;

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly IMediaSource _media;
    private readonly SurfaceRegistry _registry;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly RevisionTracker _revisions = new();
    private readonly object _gate = new();
    private IStateTopic? _topic;
    private ITimer? _driftTimer;
    private Action? _onChanged;
    private bool _started, _stopped;

    // Position the UI extrapolates from: the source position at the moment we asked the publisher to send a snapshot.
    private double _baselinePosition;
    private long _baselineStamp;

    // Cover-art probing for sources that do not mark art in Current.ThumbUrl.
    private string _probeKey = "";
    private bool _probeHasArt, _probeRunning;
    private int _probeAttempts;
    private long _probeStamp;

    public MediaController(IMessageRouter router, IStatePublisher state, IMediaSource media, SurfaceRegistry registry, TimeProvider time, ILogger<MediaController>? log = null)
    {
        _router = router;
        _state = state;
        _media = media;
        _registry = registry;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "MediaController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _topic = _state.Declare<MediaState>(Msg.MediaState, _ => Snapshot(), Coalesce);
        _onChanged = OnSourceChanged;
        _media.Changed += _onChanged;
        _router.Handle<MediaControlParams>(Msg.MediaControl, Control);
        _driftTimer = _time.CreateTimer(_ => DriftTick(), null, DriftPeriod, DriftPeriod);
        MarkBaseline(_media.Current.PositionSec);
        MaybeProbeArt(_media.Current);
    }

    public void Stop()
    {
        lock (_gate) _stopped = true;
        if (_onChanged is not null) _media.Changed -= _onChanged;
        _onChanged = null;
        _driftTimer?.Dispose();
        _driftTimer = null;
    }

    // ---- state ----

    /// <summary>Pure read of <c>Current</c>; the only state touched is the idempotent revision tracker.</summary>
    private MediaState Snapshot()
    {
        var raw = _media.Current;
        var cur = StatusSanitizer.Media(raw);
        if (!cur.Available) return cur with { ThumbUrl = null };

        var marker = raw.ThumbUrl;
        bool hasArt;
        string artKey;
        if (!string.IsNullOrEmpty(marker)) { hasArt = true; artKey = "m:" + marker; }
        else
        {
            bool probed;
            lock (_gate) probed = _probeHasArt && _probeKey == TrackKey(cur);
            hasArt = probed;
            artKey = probed ? "p" : "";
        }

        var rev = _revisions.Observe(TrackKey(cur) + "\u0001" + artKey);
        return cur with { ThumbUrl = hasArt ? DynUrls.MediaArt(rev) : null };
    }

    private static string TrackKey(MediaState s) => string.Join('\u0001', s.Title, s.Artist, s.Album);

    private void OnSourceChanged()
    {
        // Runs on an OS/WinRT thread: bookkeeping and Invalidate only.
        var cur = _media.Current;
        MarkBaseline(cur.PositionSec);
        _topic?.Invalidate();
        MaybeProbeArt(cur);
    }

    private void MarkBaseline(double positionSec)
    {
        lock (_gate)
        {
            _baselinePosition = StatusSanitizer.Seconds(positionSec);
            _baselineStamp = _time.GetTimestamp();
        }
    }

    private void DriftTick()
    {
        try
        {
            var cur = _media.Current;
            if (!cur.Available || !cur.Playing) return;
            if (!HasConsumer()) return;
            double expected;
            lock (_gate)
            {
                if (_stopped) return;
                expected = _baselinePosition + _time.GetElapsedTime(_baselineStamp).TotalSeconds;
            }
            var actual = StatusSanitizer.Seconds(cur.PositionSec);
            if (Math.Abs(actual - expected) <= DriftThresholdSec) return;
            MarkBaseline(actual);
            _topic?.Invalidate();
        }
        catch (Exception ex) { _log.LogDebug(ex, "Media drift check failed"); }
    }

    /// <summary>Someone who displays the media state is ready (menubar item, Control Center / now-playing popover, desktop widget).</summary>
    private bool HasConsumer() => _registry.ReadyChannels().Any(c => c.Id.Kind is SurfaceKind.Menubar or SurfaceKind.Overlay or SurfaceKind.Desktop);

    // ---- cover art probing ----

    private void MaybeProbeArt(MediaState raw)
    {
        if (!raw.Available || !string.IsNullOrEmpty(raw.ThumbUrl)) return;
        var key = TrackKey(StatusSanitizer.Media(raw));
        lock (_gate)
        {
            if (_stopped || _probeRunning) return;
            if (_probeKey != key) { _probeKey = key; _probeHasArt = false; _probeAttempts = 0; }
            else if (_probeHasArt || _probeAttempts >= MaxArtProbesPerTrack || _time.GetElapsedTime(_probeStamp) < ArtProbeRetryGap) return;
            _probeRunning = true;
            _probeAttempts++;
            _probeStamp = _time.GetTimestamp();
        }
        _ = Task.Run(() => ProbeArtAsync(key));
    }

    private async Task ProbeArtAsync(string key)
    {
        var has = false;
        try
        {
            using var cts = new CancellationTokenSource(ArtProbeTimeout, _time);
            var art = await _media.GetArtAsync(cts.Token).ConfigureAwait(false);
            has = art is { Data.Length: > 0 };
        }
        catch (Exception ex) { _log.LogDebug(ex, "Cover-art probe failed"); }

        bool changed;
        lock (_gate)
        {
            _probeRunning = false;
            changed = has && _probeKey == key && !_probeHasArt;
            if (changed) _probeHasArt = true;
        }
        if (changed) _topic?.Invalidate();
    }

    // ---- media.control ----

    private async Task Control(CallContext ctx, MediaControlParams p)
    {
        if (!Enum.IsDefined(p.Action)) throw ProtocolException.BadRequest("unknown media action");
        var cur = _media.Current;
        if (!cur.Available) throw ProtocolException.Unsupported("no media session");

        double? position = null;
        switch (p.Action)
        {
            case MediaAction.Play when !cur.CanPlay:
            case MediaAction.Pause when !cur.CanPause:
            case MediaAction.Next when !cur.CanNext:
            case MediaAction.Previous when !cur.CanPrevious:
            case MediaAction.Toggle when !cur.CanPlay && !cur.CanPause:
                throw ProtocolException.Unsupported($"the current media session does not support '{p.Action}'");
            case MediaAction.Seek:
                if (p.PositionSec is not { } pos || !double.IsFinite(pos)) throw ProtocolException.BadRequest("seek needs a finite positionSec");
                if (!cur.CanSeek) throw ProtocolException.Unsupported("the current media session cannot seek");
                if (pos < 0 || pos > Math.Max(0, cur.DurationSec)) throw ProtocolException.BadRequest("positionSec is outside 0..duration");
                position = pos;
                break;
        }

        try { await _media.ControlAsync(p.Action, position).WaitAsync(ctx.Cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "media.control {Action} failed", p.Action);
            throw ProtocolException.Failed($"media.control {p.Action} failed");
        }

        if (position is { } seekTo) MarkBaseline(seekTo);
        _topic?.Invalidate();
    }
}
