using System.Runtime.InteropServices;
using System.Security.AccessControl;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace BetterWindows.Native;

/// <summary>
/// Windows personalisation: app light/dark mode (HKCU Personalize), accent colour (<c>UISettings.GetColorValue(Accent)</c> with the DWM registry value as
/// fallback), reduced motion (<c>SPI_GETCLIENTAREAANIMATION</c>) and reduced transparency (transparency switched off, or high-contrast on).
/// Changes are detected by a notification thread (<c>RegNotifyChangeKeyValue</c> on the Personalize and DWM keys), by
/// <c>UISettings.ColorValuesChanged</c> and by <c>SystemEvents.UserPreferenceChanged</c> (animation settings). Changes are debounced.
/// <see cref="SetAppsUseLightTheme"/> writes the user's preference and broadcasts <c>WM_SETTINGCHANGE("ImmersiveColorSet")</c> so running apps refresh.
/// </summary>
public sealed class RegistryThemeSource : IThemeSource, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const uint WaitInfinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;
    // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET: a value written or created under the key (the notification is one-shot, so it is re-armed).
    private const uint NotifyFilter = 0x00000001 | 0x00000004;

    private readonly ILogger _log;
    private readonly StatusCell<ThemeSnapshot> _cell;
    private readonly StatusDebouncer _debouncer;
    private readonly object _gate = new();
    private readonly TypedEventHandler<UISettings, object> _onColor;
    private readonly UserPreferenceChangedEventHandler _onPreference;
    private bool _started;
    private bool _disposed;
    private UISettings? _ui;
    private SafeFileHandle? _stopEvent;
    private Thread? _notifyThread;

    public RegistryThemeSource(ILogger<RegistryThemeSource>? log = null)
    {
        _log = log ?? NullLogger<RegistryThemeSource>.Instance;
        // The constructor does no I/O: the cell starts from safe defaults and EnsureStarted performs the first real read.
        _cell = new StatusCell<ThemeSnapshot>(new ThemeSnapshot(true, ThemeMath.DefaultAccentHex, false, false), ThemeMath.Same, _log);
        _debouncer = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600),
            Refresh, ex => _log.LogWarning(ex, "Theme refresh failed"));
        _onColor = (_, _) => _debouncer.Trigger();
        _onPreference = (_, _) => _debouncer.Trigger();
    }

    /// <inheritdoc />
    public bool AppsUseLightTheme { get { EnsureStarted(); return _cell.Value.AppsUseLightTheme; } }

    /// <inheritdoc />
    public string AccentColorHex { get { EnsureStarted(); return _cell.Value.AccentColorHex; } }

    /// <inheritdoc />
    public bool ReduceMotion { get { EnsureStarted(); return _cell.Value.ReduceMotion; } }

    /// <inheritdoc />
    public bool ReduceTransparency { get { EnsureStarted(); return _cell.Value.ReduceTransparency; } }

    /// <inheritdoc />
    public event Action? Changed
    {
        add { _cell.Add(value); EnsureStarted(); }
        remove { _cell.Remove(value); }
    }

    private void EnsureStarted()
    {
        SafeFileHandle stop;
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            try
            {
                _ui = new UISettings();
                _ui.ColorValuesChanged += _onColor;
            }
            catch (Exception ex)
            {
                // UISettings is WinRT and may be unavailable in some sessions; the registry accent is the fallback.
                _log.LogDebug(ex, "UISettings is unavailable");
            }
            try
            {
                SystemEvents.UserPreferenceChanged += _onPreference;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SystemEvents is unavailable; animation changes are picked up on the next registry change");
            }
            // Manual-reset stop event: SetEvent in Dispose ends the wait for good, even if the thread is not in the wait at that moment.
            stop = CreateEvent(null, true, false, (string?)null);
            _stopEvent = stop;
            _notifyThread = new Thread(() => WatchRegistry(stop)) { IsBackground = true, Name = "BetterWindows.ThemeWatch" };
            _notifyThread.Start();
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            _cell.Publish(Read(_ui, _log));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the theme failed");
        }
    }

    /// <summary>Reads every personalisation value. Each part degrades to a safe default on failure.</summary>
    private static ThemeSnapshot Read(UISettings? ui, ILogger log)
    {
        // A missing AppsUseLightTheme value means Windows' default, which is light.
        var lightValue = ReadDword(PersonalizeKey, "AppsUseLightTheme", log);
        var transparency = ReadDword(PersonalizeKey, "EnableTransparency", log);
        return new ThemeSnapshot(
            AppsUseLightTheme: lightValue is null || lightValue != 0,
            AccentColorHex: ReadAccent(ui, log) ?? ThemeMath.DefaultAccentHex,
            ReduceMotion: ThemeMath.ReduceMotion(ReadClientAreaAnimation()),
            ReduceTransparency: ThemeMath.ReduceTransparency(transparency, IsHighContrastOn()));
    }

    private static string? ReadAccent(UISettings? ui, ILogger log)
    {
        if (ui is not null)
        {
            try
            {
                var c = ui.GetColorValue(UIColorType.Accent);
                return ThemeMath.Hex(c.R, c.G, c.B);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "UISettings accent read failed");
            }
        }
        var abgr = ReadDword(DwmKey, "AccentColor", log);
        return abgr is { } v ? ThemeMath.FromAbgr(unchecked((uint)v)) : null;
    }

    private static int? ReadDword(string subKey, string name, ILogger log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            log.LogDebug(ex, "Registry value {Name} is unreadable", name);
            return null;
        }
    }

    /// <summary>SPI_GETCLIENTAREAANIMATION: TRUE when animations are on. Null when the query fails.</summary>
    private static bool? ReadClientAreaAnimation()
    {
        var enabled = 0;
        unsafe
        {
            // The call writes a BOOL to pvParam and returns FALSE on failure.
            if (!SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0)) return null;
        }
        return enabled != 0;
    }

    private static bool IsHighContrastOn()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)Marshal.SizeOf<HIGHCONTRASTW>() };
        unsafe
        {
            // SPI_GETHIGHCONTRAST fills the struct; HCF_HIGHCONTRASTON is bit 0 of dwFlags.
            if (!SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0)) return false;
        }
        return ((uint)hc.dwFlags & 0x1) != 0;
    }

    /// <summary>
    /// Waits until <paramref name="stop"/> is signalled. Each key is opened with KEY_NOTIFY only; after every notification its event is re-armed,
    /// because <c>RegNotifyChangeKeyValue</c> fires once per arming.
    /// </summary>
    private void WatchRegistry(SafeFileHandle stop)
    {
        RegistryKey? personalize = null;
        RegistryKey? dwm = null;
        SafeFileHandle? personalizeEvent = null;
        SafeFileHandle? dwmEvent = null;
        try
        {
            personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey, RegistryRights.Notify);
            dwm = Registry.CurrentUser.OpenSubKey(DwmKey, RegistryRights.Notify);
            if (personalize is null || dwm is null)
            {
                _log.LogWarning("Theme registry keys could not be opened for change notification");
                return;
            }
            personalizeEvent = CreateEvent(null, false, false, (string?)null);
            dwmEvent = CreateEvent(null, false, false, (string?)null);
            if (!Arm(personalize, personalizeEvent) || !Arm(dwm, dwmEvent)) return;

            var handles = new HANDLE[]
            {
                new HANDLE(stop.DangerousGetHandle()),
                new HANDLE(personalizeEvent.DangerousGetHandle()),
                new HANDLE(dwmEvent.DangerousGetHandle()),
            };
            while (true)
            {
                uint r;
                unsafe
                {
                    fixed (HANDLE* h = handles)
                    {
                        r = (uint)WaitForMultipleObjects(3, h, false, WaitInfinite);
                    }
                }
                if (r == WaitObject0) break;                                          // stop requested
                if (r == WaitObject0 + 1) { if (!Arm(personalize, personalizeEvent)) break; }
                else if (r == WaitObject0 + 2) { if (!Arm(dwm, dwmEvent)) break; }
                else break;                                                          // WAIT_FAILED / abandoned: give up quietly
                _debouncer.Trigger();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Theme registry watcher stopped");
        }
        finally
        {
            personalizeEvent?.Dispose();
            dwmEvent?.Dispose();
            personalize?.Dispose();
            dwm?.Dispose();
        }
    }

    private bool Arm(RegistryKey key, SafeFileHandle evt)
    {
        var error = RegNotifyChangeKeyValue(key.Handle, false, (REG_NOTIFY_FILTER)NotifyFilter, evt, true);
        if (error == 0) return true;
        _log.LogWarning("RegNotifyChangeKeyValue failed: {Error}", (uint)error);
        return false;
    }

    /// <summary>Writes the Windows app theme preference and broadcasts the colour-set change so running apps repaint.</summary>
    public void SetAppsUseLightTheme(bool light)
    {
        if (_disposed) return;
        var value = light ? 1 : 0;
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true))
        {
            key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        }
        BroadcastImmersiveColorSet();
        _log.LogInformation("App theme set to {Mode}", light ? "light" : "dark");
        _debouncer.Trigger();
    }

    /// <summary>
    /// <c>SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG)</c>. ABORTIFHUNG with a short timeout means a
    /// hung top-level window cannot stall this call. The broadcast is advisory: an app that ignores it keeps its theme until restart.
    /// </summary>
    private void BroadcastImmersiveColorSet()
    {
        unsafe
        {
            fixed (char* p = "ImmersiveColorSet")
            {
                nuint result = 0;
                var sent = SendMessageTimeout(new HWND(new IntPtr(0xffff)), WM_SETTINGCHANGE, new WPARAM(0), new LPARAM((nint)p),
                    SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 200, &result);
                if (sent.Value == 0) _log.LogDebug("WM_SETTINGCHANGE broadcast reached no window or timed out");
            }
        }
    }

    public void Dispose()
    {
        Thread? thread;
        SafeFileHandle? stop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _notifyThread;
            stop = _stopEvent;
            _notifyThread = null;
            _stopEvent = null;
            if (_ui is not null) _ui.ColorValuesChanged -= _onColor;
            _ui = null;
        }
        try { SystemEvents.UserPreferenceChanged -= _onPreference; }
        catch (Exception ex) { _log.LogDebug(ex, "Unsubscribing from SystemEvents failed"); }
        if (stop is not null) SetEvent(stop);
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(2));
        stop?.Dispose();
        _debouncer.Dispose();
    }
}
