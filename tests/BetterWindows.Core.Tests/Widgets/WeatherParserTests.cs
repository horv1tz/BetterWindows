using BetterWindows.Core.Protocol;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>Parsing of Open-Meteo responses: the recorded live samples, plus degraded and hostile bodies.</summary>
public class WeatherParserTests
{
    private const string Stamp = "2026-10-23T12:00:00Z";

    [Fact]
    public void Recorded_forecast_parses_current_conditions()
    {
        var w = WeatherParser.ParseForecast(WeatherSamples.Forecast, "Berlin", Stamp);
        w.Should().NotBeNull();
        w!.Available.Should().BeTrue();
        w.Location.Should().Be("Berlin");
        w.TempC.Should().Be(13.5);
        w.ApparentC.Should().Be(10.6);
        w.Code.Should().Be(3);
        w.IsDay.Should().BeTrue();
        w.Humidity.Should().Be(61);
        w.WindKph.Should().Be(13.2);
        w.UpdatedAt.Should().Be(Stamp);
    }

    [Fact]
    public void Recorded_forecast_hourly_starts_at_the_current_hour_and_has_24_points()
    {
        var w = WeatherParser.ParseForecast(WeatherSamples.Forecast, "Berlin", Stamp)!;
        w.Hourly.Should().HaveCount(WeatherParser.HourlyCount);
        w.Hourly[0].Time.Should().Be("2026-10-23T15:00"); // current.time is 15:00
        w.Hourly[0].TempC.Should().Be(13.5);
        w.Hourly[0].Code.Should().Be(3);
        w.Hourly[^1].Time.Should().Be("2026-10-24T14:00");
    }

    [Fact]
    public void Recorded_forecast_daily_has_seven_days()
    {
        var w = WeatherParser.ParseForecast(WeatherSamples.Forecast, "Berlin", Stamp)!;
        w.Daily.Should().HaveCount(WeatherParser.DailyCount);
        w.Daily[0].Should().BeEquivalentTo(new WeatherDay { Date = "2026-10-23", HighC = 13.7, LowC = 6.1, Code = 61 });
        w.Daily[^1].Date.Should().Be("2026-10-29");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("<html><body>Service unavailable</body></html>")]
    [InlineData("{\"error\":true,\"reason\":\"Cannot initialize WeatherForecast\"}")]
    [InlineData("{\"latitude\":52.5}")]
    [InlineData("{\"current\":{\"time\":\"2026-10-23T15:00\"}}")]
    [InlineData("{\"current\":{\"temperature_2m\":\"warm\"}}")]
    [InlineData("{\"current\":[]}")]
    public void Bodies_without_a_usable_temperature_are_rejected(string body)
    {
        WeatherParser.ParseForecast(body, "X", Stamp).Should().BeNull();
    }

    [Fact]
    public void Missing_optional_current_values_degrade_to_defaults()
    {
        var w = WeatherParser.ParseForecast("{\"current\":{\"temperature_2m\":7}}", "Oslo", Stamp)!;
        w.TempC.Should().Be(7);
        w.ApparentC.Should().Be(7, "apparent temperature falls back to the air temperature");
        w.Code.Should().Be(0);
        w.IsDay.Should().BeTrue("a missing is_day means day");
        w.Humidity.Should().BeNull();
        w.WindKph.Should().BeNull();
        w.Hourly.Should().BeEmpty();
        w.Daily.Should().BeEmpty();
    }

    [Fact]
    public void Numeric_strings_are_accepted_and_is_day_zero_means_night()
    {
        var w = WeatherParser.ParseForecast("{\"current\":{\"temperature_2m\":\"-3.5\",\"is_day\":0,\"relative_humidity_2m\":\"80\"}}", "X", Stamp)!;
        w.TempC.Should().Be(-3.5);
        w.IsDay.Should().BeFalse();
        w.Humidity.Should().Be(80);
    }

    [Fact]
    public void Non_finite_or_boolean_numbers_do_not_count_as_temperature()
    {
        WeatherParser.ParseForecast("{\"current\":{\"temperature_2m\":\"NaN\"}}", "X", Stamp).Should().BeNull();
        WeatherParser.ParseForecast("{\"current\":{\"temperature_2m\":true}}", "X", Stamp).Should().BeNull();
    }

    [Fact]
    public void Hourly_arrays_shorter_or_mismatched_are_tolerated()
    {
        const string body = "{\"current\":{\"time\":\"2026-10-23T10:20\",\"temperature_2m\":1}," +
            "\"hourly\":{\"time\":[\"2026-10-23T09:00\",\"2026-10-23T10:00\",5,\"2026-10-23T11:00\"],\"temperature_2m\":[0,2]}," +
            "\"daily\":{\"time\":[\"2026-10-23\",\"2026-10-24\"],\"temperature_2m_max\":[3],\"temperature_2m_min\":[1,\"x\"]}}";
        var w = WeatherParser.ParseForecast(body, "X", Stamp)!;
        // 09:00 is before the current hour and is skipped; 10:00 has a temperature; 11:00 has none (array too short); the non-string entry is skipped.
        w.Hourly.Select(h => h.Time).Should().Equal("2026-10-23T10:00");
        // Day 1 has both a high and a low; day 2 has no high (array too short) and is skipped.
        w.Daily.Should().BeEquivalentTo(new[] { new WeatherDay { Date = "2026-10-23", HighC = 3, LowC = 1, Code = 0 } });
    }

    [Fact]
    public void Locations_parse_the_recorded_geocoding_response()
    {
        var list = WeatherParser.ParseLocations(WeatherSamples.Geocode, 8)!;
        list.Should().NotBeNull();
        list.Should().HaveCount(3);
        list[0].Should().BeEquivalentTo(new WeatherLocation
        {
            Name = "Berlin", Country = "Germany", Admin = "State of Berlin", Latitude = 52.52437, Longitude = 13.41053,
        });
        list[1].Admin.Should().Be("New Hampshire");
    }

    [Fact]
    public void Locations_respect_the_maximum_and_skip_incomplete_entries()
    {
        const string body = "{\"results\":[" +
            "{\"name\":\"\",\"latitude\":1,\"longitude\":1}," +
            "{\"name\":\"NoCoords\"}," +
            "{\"name\":\"Bad\",\"latitude\":95,\"longitude\":1}," +
            "\"junk\"," +
            "{\"name\":\"A\",\"latitude\":1,\"longitude\":2,\"country\":\"  \"}," +
            "{\"name\":\"B\",\"latitude\":3,\"longitude\":4}," +
            "{\"name\":\"C\",\"latitude\":5,\"longitude\":6}]}";
        var list = WeatherParser.ParseLocations(body, 2)!;
        list.Select(l => l.Name).Should().Equal("A", "B");
        list[0].Country.Should().BeNull("blank strings become null");
    }

    [Fact]
    public void A_search_without_results_is_empty_and_an_error_body_is_null()
    {
        WeatherParser.ParseLocations("{\"generationtime_ms\":0.1}", 8).Should().BeEmpty();
        WeatherParser.ParseLocations("{\"error\":true,\"reason\":\"bad\"}", 8).Should().BeNull();
        WeatherParser.ParseLocations("garbage", 8).Should().BeNull();
    }
}
