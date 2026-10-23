using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Status;

public class MediaControllerTests
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Drift = TimeSpan.FromSeconds(5);

    private static MediaState Playing(string title = "Song", double pos = 10, double dur = 200) => new()
    {
        Available = true, Title = title, Artist = "Artist", Album = "Album", AppName = "Player", Playing = true, PositionSec = pos, DurationSec = dur,
        CanPlay = true, CanPause = true, CanNext = true, CanPrevious = true, CanSeek = true,
    };

    private static CoreHarness NewHarness(MediaState? initial = null)
    {
        var h = Kit.Harness<MediaController>();
        if (initial is not null) h.Get<FakeMediaSource>().Current = initial;
        return h;
    }

    private static void Change(CoreHarness h, Func<MediaState, MediaState> f)
    {
        var src = h.Get<FakeMediaSource>();
        src.Current = f(src.Current);
        src.RaiseChanged();
        h.Time.Advance(Coalesce);
    }

    // ---- publishing ----

    [Theory]
    [InlineData(SurfaceKind.Menubar, true)]
    [InlineData(SurfaceKind.Overlay, true)]
    [InlineData(SurfaceKind.Desktop, true)]
    [InlineData(SurfaceKind.Dock, false)]
    [InlineData(SurfaceKind.Settings, false)]
    public void Media_state_is_pushed_right_after_ready_to_the_kinds_that_consume_it(SurfaceKind kind, bool expected)
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(kind, ready: false));
        s.Events(Msg.MediaState).Count.Should().Be(expected ? 1 : 0);
    }

    [Fact]
    public void The_published_state_mirrors_the_source()
    {
        using var h = NewHarness(Playing("Title", 42, 180));
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        var m = s.LastEvent<MediaState>(Msg.MediaState)!;
        m.Should().Match<MediaState>(x => x.Available && x.Title == "Title" && x.Artist == "Artist" && x.Album == "Album" && x.AppName == "Player" && x.Playing
            && x.PositionSec == 42 && x.DurationSec == 180 && x.CanSeek && x.CanNext && x.CanPrevious && x.CanPlay && x.CanPause);
    }

    [Fact]
    public void A_source_change_is_coalesced_and_published()
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        var src = h.Get<FakeMediaSource>();
        src.Current = Playing("B");
        src.RaiseChanged();
        src.Current = Playing("C");
        src.RaiseChanged();
        h.Time.Advance(Coalesce);
        s.Events(Msg.MediaState).Should().ContainSingle();
        s.LastEvent<MediaState>(Msg.MediaState)!.Title.Should().Be("C");
    }

    [Fact]
    public void An_unchanged_state_is_not_posted_again()
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        h.Get<FakeMediaSource>().RaiseChanged();
        h.Time.Advance(Coalesce);
        s.Events(Msg.MediaState).Should().BeEmpty();
    }

    [Fact]
    public void No_session_publishes_available_false_without_art()
    {
        using var h = NewHarness();
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Overlay, ready: false));
        var m = s.LastEvent<MediaState>(Msg.MediaState)!;
        m.Available.Should().BeFalse();
        m.ThumbUrl.Should().BeNull();
    }

    [Fact]
    public void Position_beyond_the_duration_is_clamped()
    {
        using var h = NewHarness(Playing(pos: 999, dur: 100));
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Overlay, ready: false));
        s.LastEvent<MediaState>(Msg.MediaState)!.PositionSec.Should().Be(100);
    }

    // ---- art revision ----

    [Fact]
    public void Art_marker_from_the_source_yields_a_dyn_art_url_with_a_revision()
    {
        using var h = NewHarness(Playing() with { ThumbUrl = "art-1" });
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl.Should().Be(DynUrls.MediaArt(1));
    }

    [Fact]
    public void The_revision_increments_only_when_title_artist_album_or_art_change()
    {
        using var h = NewHarness(Playing() with { ThumbUrl = "art-1" });
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        string? Url() => s.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl;
        Url().Should().Be(DynUrls.MediaArt(1));

        Change(h, m => m with { PositionSec = 50 });                        // position only
        Url().Should().Be(DynUrls.MediaArt(1));
        Change(h, m => m with { Playing = false, CanSeek = false });         // playback flags only
        Url().Should().Be(DynUrls.MediaArt(1));
        Change(h, m => m with { Title = "Other" });
        Url().Should().Be(DynUrls.MediaArt(2));
        Change(h, m => m with { Artist = "Someone" });
        Url().Should().Be(DynUrls.MediaArt(3));
        Change(h, m => m with { Album = "Other album" });
        Url().Should().Be(DynUrls.MediaArt(4));
        Change(h, m => m with { ThumbUrl = "art-2" });                      // same track, new picture
        Url().Should().Be(DynUrls.MediaArt(5));
        Change(h, m => m with { PositionSec = 60 });
        Url().Should().Be(DynUrls.MediaArt(5));
    }

    [Fact]
    public void A_late_surface_sees_the_same_revision_as_the_early_ones()
    {
        using var h = NewHarness(Playing() with { ThumbUrl = "art-1" });
        h.StartAll<MediaController>();
        var a = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        var b = h.Ready(h.AddSurface(SurfaceKind.Overlay, ready: false));
        var c = h.Ready(h.AddSurface(SurfaceKind.Desktop, "m1", ready: false));
        var urls = new[] { a, b, c }.Select(x => x.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl).Distinct().ToArray();
        urls.Should().ContainSingle().Which.Should().Be(DynUrls.MediaArt(1));
    }

    [Fact]
    public void Without_art_there_is_no_thumbUrl()
    {
        using var h = NewHarness(Playing());                                  // no marker, FakeMediaSource.Art is null
        h.StartAll<MediaController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_source_without_a_marker_is_probed_for_art_and_the_url_appears_when_art_exists()
    {
        using var h = NewHarness(Playing());
        h.Get<FakeMediaSource>().Art = (new byte[] { 1, 2, 3 }, "image/png");
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        await Kit.Eventually(() => { h.Time.Advance(Coalesce); return s.LastEvent<MediaState>(Msg.MediaState)?.ThumbUrl is not null; });
        s.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl.Should().StartWith("/dyn/art/");
    }

    [Fact]
    public async Task The_art_probe_is_made_once_per_track_when_there_is_no_art()
    {
        var probes = 0;
        using var h = Kit.Harness<MediaController>(sc => sc.AddSingleton<IMediaSource>(_ => new CountingMedia(() => Interlocked.Increment(ref probes))));
        var src = (CountingMedia)h.Get<IMediaSource>();
        src.Current = Playing();
        h.StartAll<MediaController>();
        await Kit.Eventually(() => probes >= 1);
        src.RaiseChanged();
        src.RaiseChanged();
        await Kit.Settle();
        probes.Should().Be(1, "retries are rate limited to once a second");
        h.Time.Advance(TimeSpan.FromSeconds(1.5));
        src.RaiseChanged();
        await Kit.Eventually(() => probes == 2);
        src.Current = Playing("Next track");
        src.RaiseChanged();
        await Kit.Eventually(() => probes == 3);
    }

    private sealed class CountingMedia : IMediaSource
    {
        private readonly Action _onProbe;
        public CountingMedia(Action onProbe) => _onProbe = onProbe;
        public MediaState Current { get; set; } = new();
        public event Action? Changed;
        public void RaiseChanged() => Changed?.Invoke();
        public Task ControlAsync(MediaAction action, double? positionSec = null) => Task.CompletedTask;
        public Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default) { _onProbe(); return Task.FromResult<(byte[] Data, string ContentType)?>(null); }
    }

    [Fact]
    public async Task A_failing_art_probe_is_survived()
    {
        using var h = Kit.Harness<MediaController>(sc => sc.AddSingleton<IMediaSource>(_ => new ThrowingArtMedia()));
        ((ThrowingArtMedia)h.Get<IMediaSource>()).Current = Playing();
        h.StartAll<MediaController>();
        await Kit.Settle();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<MediaState>(Msg.MediaState)!.ThumbUrl.Should().BeNull();
    }

    private sealed class ThrowingArtMedia : IMediaSource
    {
        public MediaState Current { get; set; } = new();
        public event Action? Changed { add { } remove { } }
        public Task ControlAsync(MediaAction action, double? positionSec = null) => Task.CompletedTask;
        public Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }

    // ---- position drift ----

    private static int Posts(FakeSurface s) => s.Events(Msg.MediaState).Count;

    [Fact]
    public void While_playing_with_a_consumer_a_position_that_drifted_more_than_2s_is_republished_within_5s()
    {
        using var h = NewHarness(Playing(pos: 10));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var before = Posts(s);
        h.Get<FakeMediaSource>().Current = Playing(pos: 40);                    // user scrubbed in the player: no Changed event raised
        h.Time.Advance(Drift);                                                  // the drift check runs and asks the publisher to resend...
        h.Time.Advance(Coalesce);                                               // ...which coalesces for 100 ms
        Posts(s).Should().Be(before + 1);
        s.LastEvent<MediaState>(Msg.MediaState)!.PositionSec.Should().Be(40);
    }

    [Fact]
    public void A_position_that_matches_what_the_ui_extrapolates_is_not_republished()
    {
        using var h = NewHarness(Playing(pos: 10));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var before = Posts(s);
        for (var i = 1; i <= 4; i++)
        {
            h.Get<FakeMediaSource>().Current = Playing(pos: 10 + 5 * i);          // the source advanced in real time
            h.Time.Advance(Drift);
        }
        h.Time.Advance(Coalesce);
        Posts(s).Should().Be(before);
    }

    [Fact]
    public void A_drift_of_exactly_two_seconds_is_tolerated_but_more_is_not()
    {
        using var h = NewHarness(Playing(pos: 10));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var before = Posts(s);
        h.Get<FakeMediaSource>().Current = Playing(pos: 10 + 5 + 2);              // expected 15, actual 17: drift 2.0
        h.Time.Advance(Drift);
        h.Time.Advance(Coalesce);
        Posts(s).Should().Be(before);
        h.Get<FakeMediaSource>().Current = Playing(pos: 10 + 10 + 2.5);           // next check (baseline unchanged): expected 20, actual 22.5: drift 2.5
        h.Time.Advance(Drift);
        h.Time.Advance(Coalesce);
        Posts(s).Should().Be(before + 1);
    }

    [Fact]
    public void Nothing_is_republished_while_paused()
    {
        using var h = NewHarness(Playing(pos: 10) with { Playing = false });
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var before = Posts(s);
        h.Get<FakeMediaSource>().Current = Playing(pos: 90) with { Playing = false };
        h.Time.Advance(TimeSpan.FromSeconds(30));
        Posts(s).Should().Be(before);
    }

    [Fact]
    public void Nothing_is_republished_without_a_consumer()
    {
        using var h = NewHarness(Playing(pos: 10));
        h.StartAll<MediaController>();
        var dock = h.AddSurface(SurfaceKind.Dock);                                // does not display media
        var notReady = h.AddSurface(SurfaceKind.Menubar, "m1", ready: false);     // not ready yet: has no listener
        h.Get<FakeMediaSource>().Current = Playing(pos: 90);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        dock.All.Should().BeEmpty();
        notReady.All.Should().BeEmpty();
        h.Ready(notReady);                                                        // the first push on ready carries the up-to-date position
        notReady.LastEvent<MediaState>(Msg.MediaState)!.PositionSec.Should().Be(90);
    }

    [Fact]
    public void Republishing_is_at_most_once_per_five_seconds()
    {
        using var h = NewHarness(Playing(pos: 10));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var before = Posts(s);
        var src = h.Get<FakeMediaSource>();
        for (var i = 0; i < 20; i++)
        {
            src.Current = Playing(pos: 100 + 50 * i);                             // wildly different every second
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }
        h.Time.Advance(Coalesce);
        Posts(s).Should().BeLessThanOrEqualTo(before + 4, "20 seconds -> at most one republish per 5 s");
        Posts(s).Should().BeGreaterThan(before);
    }

    [Fact]
    public void Stop_cancels_the_drift_timer_and_detaches_from_the_source()
    {
        using var h = NewHarness(Playing(pos: 10));
        var c = h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        c.Stop();
        var before = Posts(s);
        h.Get<FakeMediaSource>().Current = Playing("Other", pos: 90);
        h.Get<FakeMediaSource>().RaiseChanged();
        h.Time.Advance(TimeSpan.FromSeconds(30));
        Posts(s).Should().Be(before);
    }

    // ---- media.control ----

    [Theory]
    [InlineData(MediaAction.Play)]
    [InlineData(MediaAction.Pause)]
    [InlineData(MediaAction.Toggle)]
    [InlineData(MediaAction.Next)]
    [InlineData(MediaAction.Previous)]
    public async Task Transport_actions_are_forwarded_to_the_source(MediaAction action)
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = action })).Should().BeTrue();
        h.Get<FakeMediaSource>().Controls.Should().Equal(new[] { (action, (double?)null) });
    }

    [Fact]
    public async Task A_position_sent_with_a_non_seek_action_is_not_forwarded()
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Desktop, "m1");
        await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Next, PositionSec = 77 });
        h.Get<FakeMediaSource>().Controls.Should().Equal(new[] { (MediaAction.Next, (double?)null) });
    }

    [Theory]
    [InlineData(SurfaceKind.Overlay, true)]
    [InlineData(SurfaceKind.Desktop, true)]
    [InlineData(SurfaceKind.Menubar, false)]
    [InlineData(SurfaceKind.Dock, false)]
    [InlineData(SurfaceKind.Settings, false)]
    public async Task Control_is_only_allowed_from_overlay_and_desktop(SurfaceKind kind, bool allowed)
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(kind);
        var r = await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Toggle });
        if (allowed) Kit.IsOk(r).Should().BeTrue(); else Kit.ErrorCode(r).Should().Be("forbidden");
        h.Get<FakeMediaSource>().Controls.Count.Should().Be(allowed ? 1 : 0);
    }

    [Fact]
    public async Task Without_a_media_session_every_action_is_unsupported()
    {
        using var h = NewHarness();
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        foreach (var action in Enum.GetValues<MediaAction>())
            Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = action, PositionSec = 1 })).Should().Be("unsupported");
        h.Get<FakeMediaSource>().Controls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MediaAction.Play)]
    [InlineData(MediaAction.Pause)]
    [InlineData(MediaAction.Next)]
    [InlineData(MediaAction.Previous)]
    public async Task An_action_the_session_cannot_do_is_unsupported(MediaAction action)
    {
        using var h = NewHarness(Playing() with { CanPlay = false, CanPause = false, CanNext = false, CanPrevious = false });
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = action })).Should().Be("unsupported");
        h.Get<FakeMediaSource>().Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Toggle_needs_play_or_pause()
    {
        using var h = NewHarness(Playing() with { CanPlay = false, CanPause = true });
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Toggle })).Should().BeTrue();
        h.Get<FakeMediaSource>().Current = Playing() with { CanPlay = false, CanPause = false };
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Toggle })).Should().Be("unsupported");
    }

    [Fact]
    public async Task Seek_forwards_a_valid_position()
    {
        using var h = NewHarness(Playing(dur: 200));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 95.5 })).Should().BeTrue();
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 0 })).Should().BeTrue();
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 200 })).Should().BeTrue();
        h.Get<FakeMediaSource>().Controls.Should().Equal((MediaAction.Seek, (double?)95.5), (MediaAction.Seek, (double?)0), (MediaAction.Seek, (double?)200));
    }

    [Fact]
    public async Task Seek_requires_canSeek()
    {
        using var h = NewHarness(Playing() with { CanSeek = false });
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 10 })).Should().Be("unsupported");
        h.Get<FakeMediaSource>().Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Seek_validates_the_position()
    {
        using var h = NewHarness(Playing(dur: 200));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "seek" })).Should().Be("badRequest", "missing positionSec");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "seek", positionSec = -1 })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "seek", positionSec = 200.01 })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "seek", positionSec = 1e12 })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "seek", positionSec = "soon" })).Should().Be("badRequest");
        h.Get<FakeMediaSource>().Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Seek_on_media_of_unknown_duration_only_allows_zero()
    {
        using var h = NewHarness(Playing(dur: 0));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 5 })).Should().Be("badRequest");
        Kit.IsOk(await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 0 })).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_actions_are_a_badRequest()
    {
        using var h = NewHarness(Playing());
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = "explode" })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.MediaControl, new { action = 3 })).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_failing_source_becomes_failed_and_hides_the_exception_text()
    {
        using var h = Kit.Harness<MediaController>(sc => sc.AddSingleton<IMediaSource>(_ => new FailingControlMedia()));
        ((FailingControlMedia)h.Get<IMediaSource>()).Current = Playing();
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        var r = await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Next });
        Kit.ErrorCode(r).Should().Be("failed");
        ((string?)Kit.ErrorOf(r)!["message"]).Should().NotContain("SECRET");
    }

    private sealed class FailingControlMedia : IMediaSource
    {
        public MediaState Current { get; set; } = new();
        public event Action? Changed { add { } remove { } }
        public Task ControlAsync(MediaAction action, double? positionSec = null) => throw new InvalidOperationException("SECRET");
        public Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default) => Task.FromResult<(byte[] Data, string ContentType)?>(null);
    }

    [Fact]
    public async Task A_seek_resets_the_drift_baseline_so_it_is_not_republished_as_drift()
    {
        using var h = NewHarness(Playing(pos: 10, dur: 300));
        h.StartAll<MediaController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(s, Msg.MediaControl, new MediaControlParams { Action = MediaAction.Seek, PositionSec = 120 });
        h.Time.Advance(Coalesce);
        var before = Posts(s);
        h.Get<FakeMediaSource>().Current = Playing(pos: 125, dur: 300);          // the source honoured the seek and advanced 5 s
        h.Time.Advance(Drift);
        h.Time.Advance(Coalesce);
        Posts(s).Should().Be(before);
    }

    [Fact]
    public void The_media_calls_have_handlers()
    {
        using var h = NewHarness();
        h.StartAll<MediaController>();
        h.Router.RegisteredTypes.Should().Contain(Msg.MediaControl);
    }
}
