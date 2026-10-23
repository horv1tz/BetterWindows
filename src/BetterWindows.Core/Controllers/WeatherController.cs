using System.Globalization;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Widgets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owner of <c>weather.get</c>, <c>weather.search</c> and <c>weather.setLocation</c>. The location lives in <c>settings.weather</c>; the network and the 10-minute cache are
/// <see cref="WeatherService"/> (an HttpClient-injected class: a <see cref="WeatherService"/> registered in DI is used, otherwise one with the documented default
/// <see cref="SocketsHttpHandler"/> configuration is created). Nothing here throws on network trouble: the snapshot carries <c>available</c>/<c>error</c>.
/// </summary>
public sealed class WeatherController : IShellController
{
    public const int MinQueryChars = 2, MaxQueryChars = 64, MaxNameChars = 256;

    private readonly IMessageRouter _router;
    private readonly ISettingsService _settings;
    private readonly ILogger _log;
    private readonly WeatherService _service;
    private readonly bool _ownsService;

    public WeatherController(IMessageRouter router, ISettingsService settings, TimeProvider time, WeatherService? service = null, ILogger<WeatherController>? log = null)
    {
        _router = router;
        _settings = settings;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _ownsService = service is null;
        // Lazy-cheap: creating the HttpClient does no I/O.
        _service = service ?? WeatherService.CreateDefault(time, _log);
    }

    public string Name => "WeatherController";

    public void Start()
    {
        _router.Handle<WeatherGetParams, WeatherSnapshot>(Msg.WeatherGet, (ctx, p) => GetAsync(p, ctx.Cancellation));
        _router.Handle<WeatherSearchParams, WeatherLocations>(Msg.WeatherSearch, (ctx, p) => SearchAsync(p, ctx.Cancellation));
        _router.Handle<WeatherLocation>(Msg.WeatherSetLocation, (ctx, p) => SetLocationAsync(p, ctx.Cancellation));
    }

    public void Stop()
    {
        if (_ownsService) _service.Dispose();
    }

    private async Task<WeatherSnapshot> GetAsync(WeatherGetParams p, CancellationToken ct)
    {
        var w = _settings.Current.Weather;
        if (w.Latitude is not { } lat || w.Longitude is not { } lon || !IsValidCoordinate(lat, lon))
            return WeatherService.Unavailable(w.LocationName, "noLocation");
        return await _service.GetAsync(lat, lon, w.LocationName, p.Force, ct).ConfigureAwait(false);
    }

    private async Task<WeatherLocations> SearchAsync(WeatherSearchParams p, CancellationToken ct)
    {
        var query = (p.Query ?? "").Trim();
        if (query.Length < MinQueryChars || query.Length > MaxQueryChars) throw ProtocolException.BadRequest($"query must be {MinQueryChars}..{MaxQueryChars} characters");
        foreach (var c in query) if (c < 0x20 || c == 0x7f) throw ProtocolException.BadRequest("query contains control characters");

        var result = await _service.SearchAsync(query, LanguageFor(_settings.Current), ct).ConfigureAwait(false);
        if (result.Error is not null) throw ProtocolException.Failed("the weather service could not be reached (" + result.Error + ")");
        return new WeatherLocations { Locations = result.Locations.Take(WeatherService.MaxSearchResults).ToArray() };
    }

    private async Task SetLocationAsync(WeatherLocation p, CancellationToken ct)
    {
        if (!IsValidCoordinate(p.Latitude, p.Longitude)) throw ProtocolException.BadRequest("latitude must be -90..90 and longitude -180..180");
        var name = InputPolicy.Truncate((p.Name ?? "").Trim(), MaxNameChars).Trim();
        if (name.Length == 0) throw ProtocolException.BadRequest("the location needs a name");

        await _settings.UpdateAsync(s => s with { Weather = s.Weather with { LocationName = name, Latitude = p.Latitude, Longitude = p.Longitude } }, ct).ConfigureAwait(false);
        _service.Invalidate();
    }

    private static bool IsValidCoordinate(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    /// <summary>Open-Meteo language for place names: the app language, or the system UI language when set to auto. Only the two supported UI languages are used.</summary>
    internal static string LanguageFor(Settings s)
    {
        var lang = s.General.Language;
        if (lang is not ("en" or "ru")) lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return lang == "ru" ? "ru" : "en";
    }
}
