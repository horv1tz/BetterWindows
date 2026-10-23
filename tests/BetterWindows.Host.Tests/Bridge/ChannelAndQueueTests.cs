using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Host.Tests.Bridge;

public class OutboundQueueTests
{
    private static OutboundMessage State(string type, string tag) => new(OutboundKind.StateEvent, type, tag);
    private static OutboundMessage Transient(string tag) => new(OutboundKind.TransientEvent, "dock.bounce", tag);
    private static OutboundMessage Result(string tag) => new(OutboundKind.Result, null, tag);

    private static List<string> Drain(OutboundQueue q)
    {
        var list = new List<string>();
        while (q.TryDequeue(out var m)) list.Add(m.Json);
        return list;
    }

    [Fact]
    public void State_events_coalesce_to_the_latest_snapshot_of_their_type_in_order()
    {
        var q = new OutboundQueue();
        q.Enqueue(State("dock.state", "a1"));
        q.Enqueue(Result("r1"));
        q.Enqueue(State("dock.state", "a2"));
        q.Enqueue(State("status.battery", "b1"));
        q.Enqueue(State("dock.state", "a3"));
        Drain(q).Should().Equal("r1", "b1", "a3");
        q.Coalesced.Should().Be(2);
    }

    [Fact]
    public void Results_are_never_coalesced_or_dropped_below_the_hard_cap()
    {
        var q = new OutboundQueue();
        for (var i = 0; i < 100; i++) q.Enqueue(Result("r" + i));
        q.Count.Should().Be(100);
        q.Dropped.Should().Be(0);
    }

    [Fact]
    public void Transient_events_keep_only_the_newest_sixty_four()
    {
        var q = new OutboundQueue();
        for (var i = 0; i < OutboundQueue.MaxTransientEvents + 10; i++) q.Enqueue(Transient("t" + i));
        var all = Drain(q);
        all.Should().HaveCount(OutboundQueue.MaxTransientEvents);
        all[0].Should().Be("t10");
        all[^1].Should().Be("t" + (OutboundQueue.MaxTransientEvents + 9));
        q.Dropped.Should().Be(10);
    }

    [Fact]
    public void The_hard_cap_drops_the_oldest_message_of_any_kind()
    {
        var q = new OutboundQueue();
        for (var i = 0; i < OutboundQueue.MaxTotal; i++) q.Enqueue(Result("r" + i));
        q.Enqueue(Result("last"));
        q.Count.Should().Be(OutboundQueue.MaxTotal);
        q.Dropped.Should().Be(1);
        Drain(q).First().Should().Be("r1");
    }

    [Fact]
    public void Clear_empties_the_queue_and_the_transient_budget()
    {
        var q = new OutboundQueue();
        for (var i = 0; i < 60; i++) q.Enqueue(Transient("t" + i));
        q.Clear();
        q.Count.Should().Be(0);
        for (var i = 0; i < 60; i++) q.Enqueue(Transient("again" + i));
        Drain(q).Should().HaveCount(60);
    }

    [Fact]
    public void Dequeue_on_empty_is_false()
    {
        new OutboundQueue().TryDequeue(out _).Should().BeFalse();
    }
}

public class EnvelopeSnifferTests
{
    [Fact]
    public void Results_are_recognised()
    {
        var head = EnvelopeSniffer.Sniff(Json.Result("c1.2"));
        head.Kind.Should().Be(OutboundKind.Result);
        head.Type.Should().BeNull();
    }

    [Fact]
    public void State_and_transient_events_are_classified_by_their_type()
    {
        EnvelopeSniffer.Sniff(Json.Event("dock.state")).Should().Be(new OutboundHead(OutboundKind.StateEvent, "dock.state"));
        EnvelopeSniffer.Sniff(Json.Event("dock.bounce")).Kind.Should().Be(OutboundKind.TransientEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"kind\":\"event\"}")]
    [InlineData("{\"kind\":\"weird\",\"type\":\"dock.state\"}")]
    public void Anything_unrecognised_is_a_transient_event(string json)
    {
        EnvelopeSniffer.Sniff(json).Kind.Should().Be(OutboundKind.TransientEvent);
    }
}

public class SlidingWindowLimiterTests
{
    [Fact]
    public void Budget_is_per_window_and_refills_as_hits_age_out()
    {
        var time = new FakeTime();
        var limiter = new SlidingWindowLimiter(3, TimeSpan.FromSeconds(60), time);
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse("the fourth hit within the window is refused");
        time.Advance(TimeSpan.FromSeconds(30));
        limiter.TryAcquire().Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(31));
        limiter.TryAcquire().Should().BeTrue("all earlier hits are older than the window");
    }

    [Fact]
    public void Reset_clears_the_budget()
    {
        var limiter = new SlidingWindowLimiter(1, TimeSpan.FromSeconds(60), new FakeTime());
        limiter.TryAcquire();
        limiter.TryAcquire().Should().BeFalse();
        limiter.Reset();
        limiter.TryAcquire().Should().BeTrue();
    }
}

public class SurfaceHealthTests
{
    [Fact]
    public void A_surface_is_healthy_until_it_fails_and_recovers_on_ready()
    {
        var h = new SurfaceHealth(new FakeTime());
        h.IsHealthy.Should().BeTrue();
        h.IsReady.Should().BeFalse();
        h.MarkFailed();
        h.IsHealthy.Should().BeFalse();
        h.OnNavigationStarted();
        h.IsHealthy.Should().BeFalse("a hard failure stays until the next ready");
        h.OnReady();
        h.IsHealthy.Should().BeTrue();
        h.IsReady.Should().BeTrue();
    }

    [Fact]
    public void A_ready_timeout_only_counts_while_not_ready()
    {
        var h = new SurfaceHealth(new FakeTime());
        h.OnReady();
        h.OnReadyTimeout();
        h.IsHealthy.Should().BeTrue("the surface already completed its handshake");

        var late = new SurfaceHealth(new FakeTime());
        late.OnReadyTimeout();
        late.IsHealthy.Should().BeFalse();
        late.OnNavigationStarted();
        late.IsHealthy.Should().BeTrue("a new navigation clears the timeout");
    }

    [Fact]
    public void Automatic_reloads_are_budgeted_to_three_per_minute()
    {
        var time = new FakeTime();
        var h = new SurfaceHealth(time);
        h.TryBeginReload().Should().BeTrue();
        h.TryBeginReload().Should().BeTrue();
        h.TryBeginReload().Should().BeTrue();
        h.TryBeginReload().Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(61));
        h.TryBeginReload().Should().BeTrue();
    }
}

public class WebViewChannelTests
{
    private static (WebViewChannel channel, ManualUiScheduler ui, List<string> delivered, Func<ISurfaceChannel, bool> ready) Make(bool isReady = true)
    {
        var ui = new ManualUiScheduler();
        var delivered = new List<string>();
        var readyFlag = isReady;
        var channel = new WebViewChannel(new SurfaceId(SurfaceKind.Dock), ui, _ => readyFlag, json => { delivered.Add(json); return true; }, NullLogger.Instance, countTypes: true);
        return (channel, ui, delivered, _ => readyFlag);
    }

    [Fact]
    public void Nothing_reaches_the_page_before_its_document_finished_loading()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.Post(Json.Result("c1"));
        ui.RunAll();
        delivered.Should().BeEmpty();

        channel.NavigationCompleted();
        ui.RunAll();
        delivered.Should().Equal(Json.Result("c1"));
    }

    [Fact]
    public void Events_are_dropped_until_the_surface_is_ready_but_replies_are_not()
    {
        var (channel, ui, delivered, _) = Make(isReady: false);
        channel.NavigationStarted();
        channel.NavigationCompleted();
        channel.Post(Json.Event("dock.state"));
        channel.Post(Json.Result("c2"));
        ui.RunAll();
        delivered.Should().Equal(Json.Result("c2"));
        channel.DroppedNotReady.Should().Be(1);
    }

    [Fact]
    public void Delivered_events_are_counted_by_type_for_the_smoke_test()
    {
        var (channel, ui, _, _) = Make();
        channel.NavigationStarted();
        channel.NavigationCompleted();
        channel.Post(Json.Event("dock.bounce"));
        channel.Post(Json.Event("dock.bounce"));
        channel.Post(Json.Event("dock.state"));  // a state event: coalesced with nothing here, delivered once
        ui.RunAll();
        channel.DeliveredCount("dock.bounce").Should().Be(2);
        channel.DeliveredCount("dock.state").Should().Be(1);
        channel.Delivered.Should().Be(3);
    }

    [Fact]
    public void A_new_navigation_discards_what_was_queued_for_the_old_page()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.Post(Json.Result("old"));
        channel.NavigationStarted();
        channel.NavigationCompleted();
        ui.RunAll();
        delivered.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_surface_receives_nothing_and_counts_the_drops()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.NavigationCompleted();
        channel.SetFailed(true);
        channel.Post(Json.Result("c3"));
        ui.RunAll();
        delivered.Should().BeEmpty();
        channel.DroppedFailed.Should().Be(1);

        channel.SetFailed(false);
        channel.Post(Json.Result("c4"));
        ui.RunAll();
        delivered.Should().Equal(Json.Result("c4"));
    }

    [Fact]
    public void A_closed_channel_drops_everything_later()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.NavigationCompleted();
        channel.Close();
        channel.IsClosed.Should().BeTrue();
        channel.Post(Json.Result("late"));
        ui.RunAll();
        delivered.Should().BeEmpty();
    }

    [Fact]
    public void Delivery_stops_when_the_webview_is_gone_and_the_queue_is_cleared()
    {
        var ui = new ManualUiScheduler();
        var calls = 0;
        var channel = new WebViewChannel(new SurfaceId(SurfaceKind.Menubar), ui, _ => true, _ => { calls++; return false; }, NullLogger.Instance);
        channel.NavigationStarted();
        channel.NavigationCompleted();
        channel.Post(Json.Result("a"));
        channel.Post(Json.Result("b"));
        ui.RunAll();
        calls.Should().Be(1);
        channel.Pending.Should().Be(0);
    }

    [Fact]
    public void Posting_from_another_thread_only_queues_and_the_flush_runs_on_the_ui_thread()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.NavigationCompleted();
        var worker = new Thread(() => channel.Post(Json.Result("from-thread")));
        worker.Start();
        worker.Join();
        delivered.Should().BeEmpty("nothing is delivered until the UI scheduler runs the flush");
        ui.Pending.Should().BeGreaterThan(0);
        ui.RunAll();
        delivered.Should().Equal(Json.Result("from-thread"));
    }

    [Fact]
    public void Flushes_are_batched_to_the_configured_size()
    {
        var (channel, ui, delivered, _) = Make();
        channel.NavigationStarted();
        channel.NavigationCompleted();
        for (var i = 0; i < WebViewChannel.FlushBatch + 5; i++) channel.Post(Json.Result("r" + i));
        ui.RunAll();
        delivered.Should().HaveCount(WebViewChannel.FlushBatch + 5, "a flush reschedules itself until the queue is empty");
    }
}

public class WebViewBridgeHelperTests
{
    [Fact]
    public void A_page_speaking_another_protocol_version_is_a_mismatch()
    {
        WebViewBridge.HasProtocolMismatch("{\"v\":99,\"kind\":\"call\",\"id\":\"c1\",\"type\":\"x\"}", out var v).Should().BeTrue();
        v.Should().Be(99);
    }

    [Fact]
    public void The_matching_version_or_no_version_is_not_a_mismatch()
    {
        WebViewBridge.HasProtocolMismatch(Json.Event("dock.state"), out _).Should().BeFalse();
        WebViewBridge.HasProtocolMismatch("{\"kind\":\"call\"}", out _).Should().BeFalse();
        WebViewBridge.HasProtocolMismatch("[1,2]", out _).Should().BeFalse();
        WebViewBridge.HasProtocolMismatch("not json", out _).Should().BeFalse();
    }

    [Fact]
    public void Missing_runtime_is_recognised_through_inner_exceptions()
    {
        WebViewBridge.IsRuntimeMissing(new DllNotFoundException("WebView2Loader.dll")).Should().BeTrue();
        WebViewBridge.IsRuntimeMissing(new InvalidOperationException("outer", new DllNotFoundException("x"))).Should().BeTrue();
        WebViewBridge.IsRuntimeMissing(new System.Runtime.InteropServices.COMException("x", unchecked((int)0x80070002))).Should().BeTrue();
        WebViewBridge.IsRuntimeMissing(new InvalidOperationException("other")).Should().BeFalse();
    }

    [Fact]
    public void Dyn_and_static_error_headers_are_never_cached()
    {
        AppHttpResponse.ErrorHeaders().Should().Contain("Cache-Control: no-store");
        AppHttpResponse.ErrorHeaders().Should().Contain("Content-Security-Policy: default-src 'none'; sandbox");
    }
}
