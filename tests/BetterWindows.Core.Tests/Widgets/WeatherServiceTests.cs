using System.Net;
using System.Text;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Widgets;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>Scripted HTTP answers; records every request URI.</summary>
internal sealed class FakeOpenMeteo : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<Uri> _requests = new();
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = (_, _) => Task.FromResult(Json(WeatherSamples.Forecast));
    public IReadOnlyList<Uri> Requests { get { lock (_gate) return _requests.ToArray(); } }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate) _requests.Add(request.RequestUri!);
        return Respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public HttpClient Client() => new(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
}

/// <summary>The weather client: caching, single-flight, failure codes and the 8 s timeout, all on a manual clock.</summary>
public class WeatherServiceTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeOpenMeteo _http = new();

    private WeatherService Service() => new(_http.Client(), _time);

    [Fact]
    public async Task Fresh_data_is_cached_for_ten_minutes()
    {
        using var svc = Service();
        var a = await svc.GetAsync(52.52, 13.41, "Berlin", force: false);
        _time.Advance(TimeSpan.FromMinutes(9));
        var b = await svc.GetAsync(52.52, 13.41, "Berlin", force: false);
        _http.Requests.Should().HaveCount(1);
        b.Should().BeEquivalentTo(a);

        _time.Advance(TimeSpan.FromMinutes(2));
        await svc.GetAsync(52.52, 13.41, "Berlin", force: false);
        _http.Requests.Should().HaveCount(2, "the entry is older than the 10-minute TTL");
    }

    [Fact]
    public async Task Force_bypasses_the_cache()
    {
        using var svc = Service();
        await svc.GetAsync(1, 2, "A", force: false);
        await svc.GetAsync(1, 2, "A", force: true);
        _http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_location_name_is_stamped_on_every_answer_even_from_cache()
    {
        using var svc = Service();
        await svc.GetAsync(1, 2, "First", force: false);
        var again = await svc.GetAsync(1, 2, "Renamed", force: false);
        again.Location.Should().Be("Renamed");
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task The_request_uses_the_documented_forecast_query()
    {
        using var svc = Service();
        await svc.GetAsync(52.5, 13.4, "Berlin", force: false);
        var uri = _http.Requests.Single().AbsoluteUri;
        uri.Should().StartWith(WeatherService.ForecastEndpoint + "?latitude=52.5&longitude=13.4");
        uri.Should().Contain("temperature_unit=celsius").And.Contain("wind_speed_unit=kmh").And.Contain("timezone=auto");
    }

    [Fact]
    public async Task Offline_without_earlier_data_reports_offline_and_never_throws()
    {
        _http.Respond = (_, _) => throw new HttpRequestException("no route");
        using var svc = Service();
        var w = await svc.GetAsync(1, 2, "Nowhere", force: false);
        w.Available.Should().BeFalse();
        w.Error.Should().Be("offline");
        w.Location.Should().Be("Nowhere");
    }

    [Fact]
    public async Task Any_unexpected_exception_from_the_transport_is_treated_as_offline()
    {
        _http.Respond = (_, _) => throw new InvalidOperationException("boom");
        using var svc = Service();
        var w = await svc.GetAsync(1, 2, "X", force: false);
        w.Error.Should().Be("offline");
    }

    [Fact]
    public async Task When_the_network_fails_the_last_good_snapshot_is_returned_with_its_old_timestamp()
    {
        using var svc = Service();
        var good = await svc.GetAsync(1, 2, "Town", force: false);
        _time.Advance(TimeSpan.FromHours(3));
        _http.Respond = (_, _) => throw new HttpRequestException("down");
        var stale = await svc.GetAsync(1, 2, "Town", force: true);
        stale.Available.Should().BeTrue();
        stale.UpdatedAt.Should().Be(good.UpdatedAt, "the UI shows how old the data is");
    }

    [Fact]
    public async Task Server_errors_map_to_serverError()
    {
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json("{}", HttpStatusCode.InternalServerError));
        using var svc = Service();
        (await svc.GetAsync(1, 2, "X", force: false)).Error.Should().Be("serverError");
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_an_error()
    {
        _http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/") } });
        using var svc = Service();
        (await svc.GetAsync(1, 2, "X", force: false)).Error.Should().Be("serverError");
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("{\"error\":true,\"reason\":\"invalid\"}")]
    public async Task Bodies_that_are_not_a_forecast_map_to_badResponse(string body)
    {
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json(body));
        using var svc = Service();
        (await svc.GetAsync(1, 2, "X", force: false)).Error.Should().Be("badResponse");
    }

    [Fact]
    public async Task An_oversized_body_is_refused()
    {
        var big = new string('x', 2 * 1024 * 1024 + 10);
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json(big));
        using var svc = Service();
        (await svc.GetAsync(1, 2, "X", force: false)).Error.Should().Be("badResponse");
    }

    [Fact]
    public async Task The_eight_second_timeout_is_enforced_on_the_injected_clock()
    {
        var entered = new TaskCompletionSource();
        _http.Respond = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeOpenMeteo.Json(WeatherSamples.Forecast);
        };
        using var svc = Service();
        var pending = svc.GetAsync(1, 2, "Slow", force: false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _time.Advance(TimeSpan.FromSeconds(7.9));
        pending.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(0.2));
        var w = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        w.Error.Should().Be("timeout");
    }

    [Fact]
    public async Task Concurrent_requests_for_one_place_share_a_single_fetch()
    {
        var gate = new TaskCompletionSource();
        _http.Respond = async (_, _) =>
        {
            await gate.Task;
            return FakeOpenMeteo.Json(WeatherSamples.Forecast);
        };
        using var svc = Service();
        var a = svc.GetAsync(1, 2, "A", force: false);
        var b = svc.GetAsync(1, 2, "A", force: false);
        await Task.Delay(50);
        gate.SetResult();
        (await Task.WhenAll(a, b)).Should().OnlyContain(w => w.Available);
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_caller_cancellation_throws_without_cancelling_the_shared_fetch()
    {
        var gate = new TaskCompletionSource();
        _http.Respond = async (_, _) => { await gate.Task; return FakeOpenMeteo.Json(WeatherSamples.Forecast); };
        using var svc = Service();
        using var cts = new CancellationTokenSource();
        var first = svc.GetAsync(1, 2, "A", force: false, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        gate.SetResult();
        var second = await svc.GetAsync(1, 2, "A", force: false);
        second.Available.Should().BeTrue();
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Invalidate_drops_the_cache()
    {
        using var svc = Service();
        await svc.GetAsync(1, 2, "A", force: false);
        svc.Invalidate();
        await svc.GetAsync(1, 2, "A", force: false);
        _http.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public async Task Invalid_coordinates_are_refused_without_a_request(double lat, double lon)
    {
        using var svc = Service();
        var w = await svc.GetAsync(lat, lon, "X", force: false);
        w.Error.Should().Be("noLocation");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_builds_an_escaped_geocoding_request_and_parses_matches()
    {
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json(WeatherSamples.Geocode));
        using var svc = Service();
        var r = await svc.SearchAsync("Berlin & co", "ru");
        r.Error.Should().BeNull();
        r.Locations.Should().HaveCount(3);
        var uri = _http.Requests.Single().AbsoluteUri;
        uri.Should().StartWith(WeatherService.GeocodingEndpoint + "?name=Berlin%20%26%20co&count=8&language=ru");
    }

    [Fact]
    public async Task Search_failures_are_reported_not_thrown()
    {
        _http.Respond = (_, _) => throw new HttpRequestException("down");
        using var svc = Service();
        (await svc.SearchAsync("Paris", "en")).Error.Should().Be("offline");

        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json("garbage"));
        (await svc.SearchAsync("Paris", "en")).Error.Should().Be("badResponse");
    }

    [Fact]
    public async Task Search_with_no_matches_is_an_empty_success()
    {
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json("{\"generationtime_ms\":0.1}"));
        using var svc = Service();
        var r = await svc.SearchAsync("Zzzzq", "en");
        r.Error.Should().BeNull();
        r.Locations.Should().BeEmpty();
    }

    [Fact]
    public void The_default_client_does_not_follow_redirects_and_has_no_HttpClient_timeout()
    {
        using var client = WeatherService.CreateDefaultClient();
        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
        client.DefaultRequestHeaders.UserAgent.ToString().Should().Contain("BetterWindows");
    }
}
