using BetterWindows.Core.Controllers;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>weather.get / weather.search / weather.setLocation driven through the router, with the recorded Open-Meteo answers.</summary>
public sealed class WeatherControllerTests : IDisposable
{
    private readonly FakeOpenMeteo _http = new();
    private readonly CoreHarness _h;
    private readonly FakeSurface _settings;

    public WeatherControllerTests()
    {
        _h = Kit.Harness<WeatherController>(sc => sc.AddSingleton(sp => new WeatherService(_http.Client(), sp.GetRequiredService<TimeProvider>())));
        _h.Get<WeatherController>().Start();
        _settings = _h.AddSurface(SurfaceKind.Settings);
    }

    public void Dispose() => _h.Dispose();

    private TestSettings Settings => _h.Get<TestSettings>();

    private async Task<T> Ok<T>(string type, object? payload = null) => await _h.CallAsync<T>(_settings, type, payload) ?? throw new InvalidOperationException("no payload");

    private async Task<string> ErrorCode(string type, object? payload = null)
    {
        var r = await _h.CallRawAsync(_settings, type, payload);
        r["ok"]!.GetValue<bool>().Should().BeFalse();
        return (string)r["error"]!["code"]!;
    }

    private async Task SetBerlin() =>
        await _h.CallRawAsync(_settings, Msg.WeatherSetLocation, new WeatherLocation { Name = "Berlin", Latitude = 52.52, Longitude = 13.41 });

    [Fact]
    public async Task Without_a_location_the_answer_is_unavailable_and_no_request_is_made()
    {
        var w = await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        w.Available.Should().BeFalse();
        w.Error.Should().Be("noLocation");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Setting_a_location_persists_it_and_weather_is_fetched_for_it()
    {
        await SetBerlin();
        Settings.Current.Weather.LocationName.Should().Be("Berlin");
        Settings.Current.Weather.Latitude.Should().Be(52.52);
        Settings.Current.Weather.Longitude.Should().Be(13.41);

        var w = await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        w.Available.Should().BeTrue();
        w.Location.Should().Be("Berlin");
        w.TempC.Should().Be(13.5);
        _http.Requests.Should().ContainSingle().Which.AbsoluteUri.Should().Contain("latitude=52.52").And.Contain("longitude=13.41");
    }

    [Fact]
    public async Task Changing_the_location_drops_the_cache()
    {
        await SetBerlin();
        await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        _http.Requests.Should().HaveCount(1, "the second answer comes from the cache");

        await _h.CallRawAsync(_settings, Msg.WeatherSetLocation, new WeatherLocation { Name = "Paris", Latitude = 48.85, Longitude = 2.35 });
        await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        _http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Force_bypasses_the_cache_through_the_call()
    {
        await SetBerlin();
        await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams());
        await Ok<WeatherSnapshot>(Msg.WeatherGet, new WeatherGetParams { Force = true });
        _http.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-90.5, 0)]
    [InlineData(0, 180.5)]
    public async Task Coordinates_out_of_range_are_rejected(double lat, double lon)
    {
        var code = await ErrorCode(Msg.WeatherSetLocation, new WeatherLocation { Name = "X", Latitude = lat, Longitude = lon });
        code.Should().Be("badRequest");
        Settings.Current.Weather.LocationName.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_location_needs_a_printable_name(string name)
    {
        var code = await ErrorCode(Msg.WeatherSetLocation, new WeatherLocation { Name = name, Latitude = 1, Longitude = 1 });
        code.Should().Be("badRequest");
    }

    [Fact]
    public async Task Control_characters_in_a_location_name_are_replaced_by_spaces()
    {
        await _h.CallRawAsync(_settings, Msg.WeatherSetLocation, new WeatherLocation { Name = "Ber\u0007lin\nX", Latitude = 1, Longitude = 1 });
        Settings.Current.Weather.LocationName.Should().Be("Ber lin X");
    }

    [Fact]
    public async Task Over_long_location_names_are_truncated_not_rejected()
    {
        var longName = new string('B', 400);
        await _h.CallRawAsync(_settings, Msg.WeatherSetLocation, new WeatherLocation { Name = longName, Latitude = 1, Longitude = 1 });
        Settings.Current.Weather.LocationName!.Length.Should().Be(WeatherController.MaxNameChars);
    }

    [Theory]
    [InlineData("")]
    [InlineData("B")]
    [InlineData("Be\nrlin")]
    public async Task Search_queries_are_validated(string query)
    {
        (await ErrorCode(Msg.WeatherSearch, new WeatherSearchParams { Query = query })).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_search_query_that_is_too_long_is_rejected()
    {
        (await ErrorCode(Msg.WeatherSearch, new WeatherSearchParams { Query = new string('x', WeatherController.MaxQueryChars + 1) })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Search_returns_the_recorded_matches_in_the_app_language()
    {
        _h.Get<TestSettings>().Set(s => s with { General = s.General with { Language = "ru" } });
        _http.Respond = (_, _) => Task.FromResult(FakeOpenMeteo.Json(WeatherSamples.Geocode));

        var r = await Ok<WeatherLocations>(Msg.WeatherSearch, new WeatherSearchParams { Query = "  Berlin  " });
        r.Locations.Should().HaveCount(3);
        r.Locations[0].Name.Should().Be("Berlin");
        _http.Requests.Should().ContainSingle().Which.AbsoluteUri.Should().Contain("name=Berlin&").And.Contain("language=ru");
    }

    [Fact]
    public async Task A_search_the_service_cannot_answer_is_a_failed_call()
    {
        _http.Respond = (_, _) => throw new HttpRequestException("down");
        (await ErrorCode(Msg.WeatherSearch, new WeatherSearchParams { Query = "Berlin" })).Should().Be("failed");
    }

    [Fact]
    public async Task Location_changes_are_forbidden_from_the_desktop_surface()
    {
        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        (await _h.CallRawAsync(desktop, Msg.WeatherSetLocation, new WeatherLocation { Name = "X", Latitude = 1, Longitude = 1 }))["error"]!["code"]!
            .GetValue<string>().Should().Be("forbidden");
    }
}
