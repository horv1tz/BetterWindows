using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Native;

/// <summary>
/// Notification-area host, honest minimal version: <see cref="IsSupported"/> is false. While Explorer runs, applications send Shell_NotifyIcon messages
/// to Explorer's own Shell_TrayWnd, so a second process cannot receive them, and injecting into Explorer is not acceptable. The host therefore reports
/// no icons and ignores clicks; Start and Stop only log once. The UI offers "Show Windows taskbar" to reach tray icons. Diagnostics facts come from
/// <see cref="TrayDiagnostics"/>.
/// </summary>
public sealed class TrayHost : ITrayHost
{
    private readonly ILogger? _log;
    private int _startLogged;

    /// <summary>Creates the host. Does no I/O.</summary>
    public TrayHost(ILogger<TrayHost>? logger = null) => _log = logger;

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public void Start()
    {
        if (Interlocked.Exchange(ref _startLogged, 1) == 0)
        {
            _log?.LogInformation("Tray hosting is unsupported while Explorer owns the notification area; no tray icons will be reported");
        }
    }

    /// <inheritdoc />
    public void Stop() { }

    /// <inheritdoc />
    public IReadOnlyList<TrayIcon> Icons => Array.Empty<TrayIcon>();

    /// <inheritdoc />
    /// <remarks>Never raised, because <see cref="Icons"/> never changes.</remarks>
    public event Action? Changed { add { } remove { } }

    /// <inheritdoc />
    public Task<byte[]?> GetIconPngAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

    /// <inheritdoc />
    public void Click(string id, TrayButton button, NativePoint screenPx) { }

    /// <inheritdoc />
    public void Dispose() { }
}
