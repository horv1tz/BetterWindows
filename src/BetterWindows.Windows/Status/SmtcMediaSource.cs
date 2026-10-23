using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace BetterWindows.Native;

/// <summary>
/// The system's current media session (SMTC, <c>GlobalSystemMediaTransportControlsSessionManager</c>): metadata, playback status, timeline and transport
/// commands. The session is re-bound when the current session app changes; per-session events and the manager's session events trigger a debounced
/// refresh. Every WinRT call has a timeout. Cover art is returned by <see cref="GetArtAsync"/> (cached per track, capped at 2 MB); the protocol's
/// <c>thumbUrl</c> is built by the Core from that.
/// </summary>
public sealed class SmtcMediaSource : IMediaSource, IDisposable
{
    private const long MaxArtBytes = 2 * 1024 * 1024;

    private readonly ILogger _log;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly StatusCell<MediaState> _cell;
    private readonly StatusDebouncer _debouncer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onCurrent;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _onSessions;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProperties;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;
    private bool _started;
    private bool _disposed;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _sessionAumid;
    private (string Key, byte[] Data, string ContentType)? _artCache;

    public SmtcMediaSource(ILogger<SmtcMediaSource>? log = null)
    {
        _log = log ?? NullLogger<SmtcMediaSource>.Instance;
        _cell = new StatusCell<MediaState>(new MediaState(), SmtcMath.Same, _log);
        _debouncer = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(500),
            () => _ = RefreshAsync(), ex => _log.LogWarning(ex, "Media refresh trigger failed"));
        _onCurrent = (_, _) => _debouncer.Trigger();
        _onSessions = (_, _) => _debouncer.Trigger();
        _onProperties = (_, _) => _debouncer.Trigger();
        _onPlayback = (_, _) => _debouncer.Trigger();
        _onTimeline = (_, _) => _debouncer.Trigger();
    }

    /// <inheritdoc />
    public MediaState Current { get { EnsureStarted(); return _cell.Value; } }

    /// <inheritdoc />
    public event Action? Changed
    {
        add { _cell.Add(value); EnsureStarted(); }
        remove { _cell.Remove(value); }
    }

    private void EnsureStarted()
    {
        lock (_stateLock)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var manager = await StatusAsync.TryAsync(GlobalSystemMediaTransportControlsSessionManager.RequestAsync(), StatusAsync.DefaultTimeout, _log,
            "Media session manager request").ConfigureAwait(false);
        if (manager is null) return;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }
            _manager = manager;
            manager.CurrentSessionChanged += _onCurrent;
            manager.SessionsChanged += _onSessions;
        }
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var manager = _manager;
            var session = manager?.GetCurrentSession();
            if (session is null)
            {
                BindSession(null, null);
                _cell.Publish(new MediaState { Available = false, Playing = false });
                return;
            }
            var aumid = session.SourceAppUserModelId;
            if (_session is null || !string.Equals(aumid, _sessionAumid, StringComparison.OrdinalIgnoreCase)) BindSession(session, aumid);
            _cell.Publish(await BuildStateAsync(session, aumid).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Media refresh failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Moves the per-session event subscriptions to <paramref name="session"/> (null detaches).</summary>
    private void BindSession(GlobalSystemMediaTransportControlsSession? session, string? aumid)
    {
        lock (_stateLock)
        {
            if (_session is not null)
            {
                try
                {
                    _session.MediaPropertiesChanged -= _onProperties;
                    _session.PlaybackInfoChanged -= _onPlayback;
                    _session.TimelinePropertiesChanged -= _onTimeline;
                }
                catch (Exception ex) { _log.LogDebug(ex, "Detaching media session events failed"); }
            }
            _session = session;
            _sessionAumid = aumid;
            if (session is not null)
            {
                try
                {
                    session.MediaPropertiesChanged += _onProperties;
                    session.PlaybackInfoChanged += _onPlayback;
                    session.TimelinePropertiesChanged += _onTimeline;
                }
                catch (Exception ex) { _log.LogDebug(ex, "Attaching media session events failed"); }
            }
        }
    }

    private async Task<MediaState> BuildStateAsync(GlobalSystemMediaTransportControlsSession session, string aumid)
    {
        var props = await StatusAsync.TryAsync(session.TryGetMediaPropertiesAsync(), StatusAsync.DefaultTimeout, _log, "Media properties").ConfigureAwait(false);
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var controls = playback.Controls;

        var playing = SmtcMath.IsPlaying((int)playback.PlaybackStatus);
        var duration = SmtcMath.DurationSeconds(timeline.StartTime, timeline.EndTime);
        var position = SmtcMath.PositionSeconds(timeline.Position, timeline.LastUpdatedTime, playing, _time.GetUtcNow(), duration);

        return new MediaState
        {
            Available = true,
            Title = NonEmpty(props?.Title),
            Artist = NonEmpty(props?.Artist),
            Album = NonEmpty(props?.AlbumTitle),
            AppName = SmtcMath.AppNameFromAumid(aumid),
            Playing = playing,
            PositionSec = position,
            DurationSec = duration,
            ThumbUrl = null,
            CanPlay = controls.IsPlayEnabled,
            CanPause = controls.IsPauseEnabled,
            CanNext = controls.IsNextEnabled,
            CanPrevious = controls.IsPreviousEnabled,
            CanSeek = controls.IsPlaybackPositionEnabled,
        };
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <inheritdoc />
    public async Task ControlAsync(MediaAction action, double? positionSec = null)
    {
        var manager = _manager;
        var session = manager?.GetCurrentSession();
        if (session is null) return;

        var t = StatusAsync.DefaultTimeout;
        switch (action)
        {
            case MediaAction.Play:
                await StatusAsync.TryAsync(session.TryPlayAsync(), t, _log, "Play").ConfigureAwait(false);
                break;
            case MediaAction.Pause:
                await StatusAsync.TryAsync(session.TryPauseAsync(), t, _log, "Pause").ConfigureAwait(false);
                break;
            case MediaAction.Toggle:
                await StatusAsync.TryAsync(session.TryTogglePlayPauseAsync(), t, _log, "Toggle").ConfigureAwait(false);
                break;
            case MediaAction.Next:
                await StatusAsync.TryAsync(session.TrySkipNextAsync(), t, _log, "Next").ConfigureAwait(false);
                break;
            case MediaAction.Previous:
                await StatusAsync.TryAsync(session.TrySkipPreviousAsync(), t, _log, "Previous").ConfigureAwait(false);
                break;
            case MediaAction.Seek:
                if (SmtcMath.SeekTicks(positionSec) is { } ticks)
                    await StatusAsync.TryAsync(session.TryChangePlaybackPositionAsync(ticks), t, _log, "Seek").ConfigureAwait(false);
                break;
            default:
                return;
        }
        _debouncer.Trigger();
    }

    /// <inheritdoc />
    public async Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default)
    {
        var session = _session;
        if (session is null) return null;
        var props = await StatusAsync.TryAsync(session.TryGetMediaPropertiesAsync(), StatusAsync.DefaultTimeout, _log, "Media art properties", ct)
            .ConfigureAwait(false);
        var reference = props?.Thumbnail;
        if (reference is null) return null;

        // The cache key is the track identity: the same art is never decoded twice for one track.
        var key = $"{props!.Title}\u001f{props.Artist}\u001f{props.AlbumTitle}\u001f{_sessionAumid}";
        var cached = _artCache;
        if (cached is { } c && c.Key == key) return (c.Data, c.ContentType);

        using var stream = await StatusAsync.TryAsync(reference.OpenReadAsync(), StatusAsync.DefaultTimeout, _log, "Open cover art", ct).ConfigureAwait(false);
        if (stream is null) return null;
        var contentType = stream.ContentType ?? string.Empty;
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        if (stream.Size == 0 || stream.Size > (ulong)MaxArtBytes) return null;

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await StatusAsync.TryAsync(reader.LoadAsync((uint)stream.Size), StatusAsync.DefaultTimeout, _log, "Read cover art", ct).ConfigureAwait(false);
        if (loaded == 0) return null;
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);

        _artCache = (key, bytes, contentType);
        return (bytes, contentType);
    }

    public void Dispose()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            manager = _manager;
            _manager = null;
            if (manager is not null)
            {
                try
                {
                    manager.CurrentSessionChanged -= _onCurrent;
                    manager.SessionsChanged -= _onSessions;
                }
                catch (Exception ex) { _log.LogDebug(ex, "Detaching media manager events failed"); }
            }
        }
        BindSession(null, null);
        _debouncer.Dispose();
        _artCache = null;
    }
}
