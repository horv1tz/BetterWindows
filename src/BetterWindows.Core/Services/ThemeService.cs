using System.Text.RegularExpressions;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Services;

/// <summary>
/// Effective <see cref="ThemeState"/> = settings.appearance + Windows personalisation (<see cref="IThemeSource"/>): mode "auto" follows the Windows app theme, accent "system"
/// follows the Windows accent colour, reduceMotion/reduceTransparency are true if the setting OR Windows asks for it. <see cref="Changed"/> fires (only when the effective value
/// actually changed) after a settings change or a Windows personalisation change. Port events arrive on OS threads, so they are re-evaluated on the thread pool.
/// </summary>
public sealed partial class ThemeService : IThemeService
{
    private const string FallbackAccent = "#0a84ff";
    private readonly ISettingsService _settings;
    private readonly IThemeSource _source;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private string _lastJson;
    private ThemeState _last;
    private int _raising, _pending;

    public ThemeService(ISettingsService settings, IThemeSource source, ILogger<ThemeService>? log = null)
    {
        _settings = settings;
        _source = source;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _last = Compute();
        _lastJson = ProtocolJson.Serialize(_last);
        _settings.Changed += _ => Recompute();
        _source.Changed += () => _ = Task.Run(Recompute); // OS thread: never read the registry-backed getters here
    }

    public event Action<ThemeState>? Changed;

    /// <summary>Always computed from the live inputs, so a reader never sees a stale value between a source change and its event.</summary>
    public ThemeState Current => Compute();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    private ThemeState Compute()
    {
        var a = _settings.Current.Appearance;
        bool light = true, reduceMotion = false, reduceTransparency = false;
        string systemAccent = FallbackAccent;
        try { light = _source.AppsUseLightTheme; } catch (Exception ex) { _log.LogDebug(ex, "AppsUseLightTheme unavailable"); }
        try { systemAccent = _source.AccentColorHex; } catch (Exception ex) { _log.LogDebug(ex, "AccentColorHex unavailable"); }
        try { reduceMotion = _source.ReduceMotion; } catch (Exception ex) { _log.LogDebug(ex, "ReduceMotion unavailable"); }
        try { reduceTransparency = _source.ReduceTransparency; } catch (Exception ex) { _log.LogDebug(ex, "ReduceTransparency unavailable"); }

        var mode = a.Mode switch
        {
            ThemePreference.Light => ThemeMode.Light,
            ThemePreference.Dark => ThemeMode.Dark,
            _ => light ? ThemeMode.Light : ThemeMode.Dark,
        };
        var accent = a.Accent == "system" ? systemAccent : a.Accent;
        if (string.IsNullOrEmpty(accent) || !HexColor().IsMatch(accent)) accent = FallbackAccent;
        return new ThemeState { Mode = mode, Accent = accent, ReduceMotion = a.ReduceMotion || reduceMotion, ReduceTransparency = a.ReduceTransparency || reduceTransparency };
    }

    private void Recompute()
    {
        try
        {
            ThemeState next;
            lock (_lock)
            {
                next = Compute();
                var json = ProtocolJson.Serialize(next); // compare as JSON, never with ==
                if (json == _lastJson) return;
                _last = next;
                _lastJson = json;
            }
            RaiseChanged();
        }
        catch (Exception ex) { _log.LogError(ex, "Theme recompute failed"); }
    }

    /// <summary>Single-flight in-order delivery of the newest value outside the lock (same pattern as the settings store).</summary>
    private void RaiseChanged()
    {
        Volatile.Write(ref _pending, 1);
        while (true)
        {
            if (Interlocked.CompareExchange(ref _raising, 1, 0) != 0) return;
            try
            {
                while (Interlocked.Exchange(ref _pending, 0) == 1)
                {
                    ThemeState value;
                    lock (_lock) value = _last;
                    var handlers = Changed;
                    if (handlers is null) continue;
                    foreach (var d in handlers.GetInvocationList())
                    {
                        try { ((Action<ThemeState>)d)(value); }
                        catch (Exception ex) { _log.LogError(ex, "A theme Changed subscriber threw"); }
                    }
                }
            }
            finally { Volatile.Write(ref _raising, 0); }
            if (Volatile.Read(ref _pending) == 0) return;
        }
    }
}
