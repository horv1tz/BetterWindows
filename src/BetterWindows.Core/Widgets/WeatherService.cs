using System.Globalization;
using System.Net;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Widgets;

/// <summary>Result of a city search: either the (possibly empty) matches or an error code (never both).</summary>
public sealed record WeatherSearchResult(IReadOnlyList<WeatherLocation> Locations, string? Error)
{
    public static WeatherSearchResult Fail(string error) => new(Array.Empty<WeatherLocation>(), error);
}

/// <summary>
/// Open-Meteo client (no API key): forecast (current + next 24 hours + 7 days, Celsius, km/h) and city geocoding. Contract: it NEVER throws except for the caller's own
/// cancellation. Fresh data is cached for 10 minutes (<c>force</c> bypasses); concurrent requests for the same place share ONE request; when the network fails the last good
/// snapshot is returned (still <c>available</c>, with its old <c>updatedAt</c>), and only without any earlier data does the answer become <c>available=false</c> with an error
/// code: <c>offline</c> (connection failed), <c>timeout</c> (8 s), <c>serverError</c> (non-2xx), <c>badResponse</c> (not the expected JSON).
/// All clocks are the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class WeatherService : IDisposable
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    public const int MaxSearchResults = 8;
    public const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";
    public const string GeocodingEndpoint = "https://geocoding-api.open-meteo.com/v1/search";

    private sealed record CacheEntry(WeatherSnapshot Snapshot, DateTimeOffset FetchedAt);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<WeatherSnapshot>> _inFlight = new(StringComparer.Ordinal);
    private int _generation;

    /// <param name="http">Used as is; its own <c>Timeout</c> should be infinite (the service enforces the 8 s limit through the injected clock). Tests pass a fake handler.</param>
    public WeatherService(HttpClient http, TimeProvider time, ILogger<WeatherService>? log = null) : this(http, time, log, false) { }

    private WeatherService(HttpClient http, TimeProvider time, ILogger? log, bool ownsHttp)
    {
        _http = http;
        _time = time;
        _ownsHttp = ownsHttp;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>A service with its own HttpClient (see <see cref="CreateDefaultClient"/>); used when none is registered in DI.</summary>
    public static WeatherService CreateDefault(TimeProvider time, ILogger? log = null) => new(CreateDefaultClient(), time, log, true);

    /// <summary>
    /// The shared HTTP configuration: <see cref="SocketsHttpHandler"/> with pooled connections recycled every 5 minutes (so DNS changes are honoured), an 8 s connect timeout,
    /// automatic gzip/deflate/brotli, NO redirects (the two Open-Meteo hosts answer directly; a redirect would be a downgrade/hijack), the system proxy, and an infinite
    /// <c>HttpClient.Timeout</c> because the per-request timeout is enforced by the service on the injected clock.
    /// </summary>
    public static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = RequestTimeout,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterWindows/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>Drops every cached snapshot (the location changed). In-flight requests of the old location are ignored when they complete.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
            _inFlight.Clear();
            _generation++;
        }
    }

    /// <summary>The weather for a place. Never throws (except <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled).</summary>
    public async Task<WeatherSnapshot> GetAsync(double latitude, double longitude, string locationName, bool force, CancellationToken ct = default)
    {
        if (!(double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180))
            return Unavailable(locationName, "noLocation");

        var key = Key(latitude, longitude);
        Task<WeatherSnapshot> work;
        lock (_gate)
        {
            if (!force && _cache.TryGetValue(key, out var hit) && _time.GetUtcNow() - hit.FetchedAt < CacheTtl)
                return hit.Snapshot with { Location = locationName };
            if (!_inFlight.TryGetValue(key, out work!))
            {
                // The shared request must not die with the first caller's cancellation: it has its own timeout only.
                var generation = _generation;
                work = Task.Run(() => FetchAsync(key, latitude, longitude, locationName, generation));
                _inFlight[key] = work;
            }
        }
        var snapshot = await work.WaitAsync(ct).ConfigureAwait(false);
        return snapshot with { Location = locationName };
    }

    private async Task<WeatherSnapshot> FetchAsync(string key, double lat, double lon, string locationName, int generation)
    {
        try
        {
            var (body, error) = await GetBodyAsync(ForecastUri(lat, lon)).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            WeatherSnapshot? parsed = body is null ? null : WeatherParser.ParseForecast(body, locationName, Iso(now));
            if (body is not null && parsed is null) error = "badResponse";

            lock (_gate)
            {
                if (parsed is not null)
                {
                    if (generation == _generation) _cache[key] = new CacheEntry(parsed, now);
                    return parsed;
                }
                // Offline/failed: the last good snapshot (any age) is still the best answer; its updatedAt tells the UI how old it is.
                if (generation == _generation && _cache.TryGetValue(key, out var stale)) return stale.Snapshot;
            }
            return Unavailable(locationName, error ?? "badResponse");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Weather fetch failed unexpectedly");
            return Unavailable(locationName, "offline");
        }
        finally
        {
            // Single-flight per key; an Invalidate() in between bumped the generation and may have registered a NEWER request under the same key.
            lock (_gate) { if (generation == _generation) _inFlight.Remove(key); }
        }
    }

    /// <summary>City search. Never throws (except caller cancellation); the language selects the names Open-Meteo returns.</summary>
    public async Task<WeatherSearchResult> SearchAsync(string query, string language, CancellationToken ct = default)
    {
        var uri = $"{GeocodingEndpoint}?name={Uri.EscapeDataString(query)}&count={MaxSearchResults}&language={Uri.EscapeDataString(language)}&format=json";
        var (body, error) = await GetBodyAsync(uri, ct).ConfigureAwait(false);
        if (body is null) return WeatherSearchResult.Fail(error ?? "badResponse");
        var list = WeatherParser.ParseLocations(body, MaxSearchResults);
        return list is null ? WeatherSearchResult.Fail("badResponse") : new WeatherSearchResult(list, null);
    }

    /// <summary>GET with the 8 s timeout; (body, null) on success, (null, errorCode) otherwise. Only the caller's token escapes as an exception.</summary>
    private async Task<(string? Body, string? Error)> GetBodyAsync(string uri, CancellationToken callerToken = default)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, callerToken);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Open-Meteo answered HTTP {Status}", (int)resp.StatusCode);
                return (null, "serverError");
            }
            var body = await ReadCappedAsync(resp, linked.Token).ConfigureAwait(false);
            return body is null ? (null, "badResponse") : (body, null);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (null, "timeout"); }
        catch (HttpRequestException ex) { _log.LogInformation("Open-Meteo unreachable: {Message}", ex.Message); return (null, "offline"); }
        catch (Exception ex)
        {
            _log.LogInformation("Open-Meteo request failed: {Message}", ex.Message);
            return (null, "offline");
        }
    }

    private const int MaxBodyBytes = 2 * 1024 * 1024;

    /// <summary>Reads at most 2 MB (a forecast is ~20 KB): a hostile/broken endpoint cannot make us buffer unbounded data.</summary>
    private static async Task<string?> ReadCappedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentLength is > MaxBodyBytes) return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > MaxBodyBytes) return null;
            ms.Write(buffer, 0, n);
        }
        return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static string ForecastUri(double lat, double lon) =>
        $"{ForecastEndpoint}?latitude={lat.ToString("0.####", CultureInfo.InvariantCulture)}&longitude={lon.ToString("0.####", CultureInfo.InvariantCulture)}" +
        "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m" +
        "&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min" +
        "&forecast_days=7&temperature_unit=celsius&wind_speed_unit=kmh&timezone=auto";

    private static string Key(double lat, double lon) => lat.ToString("0.####", CultureInfo.InvariantCulture) + "," + lon.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static WeatherSnapshot Unavailable(string locationName, string error) => new() { Available = false, Error = error, Location = locationName };

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
