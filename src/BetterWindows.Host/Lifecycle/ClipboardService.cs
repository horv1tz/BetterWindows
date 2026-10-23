using System.Runtime.InteropServices;
using System.Windows;
using BetterWindows.Core.Platform;
using BetterWindows.Host.Bridge;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// <see cref="IClipboard"/>: the WPF clipboard needs an STA thread, so the write is posted to the UI thread and the caller never blocks. Another process holding the clipboard
/// open surfaces as <c>CLIPBRD_E_CANT_OPEN</c>; the write is retried a few times with a short back-off (<see cref="ClipboardRetry"/>).
/// </summary>
public sealed class ClipboardService : IClipboard
{
    /// <summary>Texts longer than this are cut (a UI bug must not park megabytes on the system clipboard).</summary>
    public const int MaxChars = 1 << 20;

    private readonly IUiScheduler _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<ClipboardService> _log;

    public ClipboardService(IUiScheduler ui, TimeProvider time, ILogger<ClipboardService> log)
    {
        _ui = ui;
        _time = time;
        _log = log;
    }

    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var value = text.Length > MaxChars ? text[..MaxChars] : text;
        _ui.Post(() => _ = WriteAsync(value));
    }

    private async Task WriteAsync(string value)
    {
        try
        {
            var ok = await ClipboardRetry.RunAsync(
                () => Clipboard.SetDataObject(value, copy: true),
                delay => Task.Delay(delay, _time),
                ClipboardRetry.MaxAttempts).ConfigureAwait(true);
            if (!ok) _log.LogWarning("The clipboard stayed locked by another process; the text was not copied");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Copying to the clipboard failed");
        }
    }
}

/// <summary>Retry loop for clipboard writes (pure: the write and the delay are injected).</summary>
public static class ClipboardRetry
{
    /// <summary>OLE: the clipboard is open in another process.</summary>
    public const int ClipbrdECantOpen = unchecked((int)0x800401D0);
    public const int MaxAttempts = 5;

    /// <summary>
    /// Runs <paramref name="write"/> up to <paramref name="maxAttempts"/> times while it throws <c>CLIPBRD_E_CANT_OPEN</c>, waiting 50, 100, 150... ms between attempts.
    /// Returns true on success, false when every attempt hit the lock; any other exception propagates.
    /// </summary>
    public static async Task<bool> RunAsync(Action write, Func<TimeSpan, Task> delay, int maxAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                write();
                return true;
            }
            catch (COMException ex) when (ex.HResult == ClipbrdECantOpen)
            {
                if (attempt == maxAttempts) return false;
                await delay(TimeSpan.FromMilliseconds(50 * attempt)).ConfigureAwait(true);
            }
        }
        return false;
    }
}
