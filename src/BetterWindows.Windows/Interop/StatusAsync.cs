using Microsoft.Extensions.Logging;
using Windows.Foundation;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Awaits WinRT asynchronous operations with a hard timeout. Many WinRT status APIs (radios, device enumeration, SMTC) are backed by system
/// services that can stall (service restarting, device driver hung); the status layer must never wait forever, so EVERY async WinRT call goes through here.
/// On timeout the underlying operation is cancelled (<c>AsTask(token)</c> calls <c>IAsyncInfo.Cancel</c>) and a default value is returned.
/// </summary>
internal static class StatusAsync
{
    /// <summary>Default budget for a single WinRT call.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Awaits <paramref name="op"/>; returns default on timeout, cancellation or failure (logged at Debug/Warning). Never throws.</summary>
    public static async Task<T?> TryAsync<T>(IAsyncOperation<T> op, TimeSpan timeout, ILogger log, string what, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await op.AsTask(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) log.LogDebug("{What} cancelled", what);
            else log.LogWarning("{What} timed out after {Timeout} ms", what, (int)timeout.TotalMilliseconds);
            return default;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{What} failed", what);
            return default;
        }
    }

    /// <summary>Awaits an <see cref="IAsyncAction"/> with a timeout. Returns true when it completed successfully.</summary>
    public static async Task<bool> TryAsync(IAsyncAction op, TimeSpan timeout, ILogger log, string what, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await op.AsTask(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) log.LogDebug("{What} cancelled", what);
            else log.LogWarning("{What} timed out after {Timeout} ms", what, (int)timeout.TotalMilliseconds);
            return false;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{What} failed", what);
            return false;
        }
    }
}
