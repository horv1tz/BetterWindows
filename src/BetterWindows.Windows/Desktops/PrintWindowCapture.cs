using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Native;

/// <summary>
/// Window thumbnails for Mission Control, produced with PrintWindow(PW_RENDERFULLCONTENT) into a 32 bpp top-down DIB section, then cropped to the
/// visible frame (DWMWA_EXTENDED_FRAME_BOUNDS), checked for all-black output, box-filter downscaled and encoded as PNG by <see cref="PngEncoder"/>.
/// PrintWindow asks the window to paint itself into our DC. PW_RENDERFULLCONTENT (2) is needed for DirectComposition and many GPU-rendered windows on
/// Windows 8.1 and later; without it those windows come back black. Cloaked windows (other virtual desktops, suspended UWP) and minimised windows return null:
/// DWM does not compose their content, and minimised windows are never restored. A hung window can block PrintWindow indefinitely, so every capture runs on
/// its own worker thread with a 500 ms budget; a worker that overruns is abandoned and keeps its concurrency slot until it returns.
/// </summary>
public sealed class PrintWindowCapture : IWindowCapture
{
    /// <summary>Per-window budget on the worker. Core keeps the last good image when a capture times out.</summary>
    internal const int PerWindowTimeoutMs = 500;

    /// <summary>Global limit of captures in flight (also counts abandoned workers).</summary>
    internal const int MaxConcurrentCaptures = 3;

    /// <summary>Requested widths are clamped into this range (thumbnails are small; larger requests are not needed).</summary>
    internal const int MinWidth = 16;
    internal const int MaxWidth = 2048;

    /// <summary>Sanity caps for the source window. Larger windows are refused rather than allocated.</summary>
    internal const int MaxSourceEdge = 8192;
    internal const long MaxSourcePixels = 9_000_000; // a 4K window (3840x2160 = 8.3 M px) fits; 3 captures in flight stay near 100 MB

    private const int GateWaitMs = 500;
    private readonly SemaphoreSlim _gate = new(MaxConcurrentCaptures, MaxConcurrentCaptures);
    private readonly ILogger? _log;

    /// <summary>Creates the capture service. Does no I/O.</summary>
    public PrintWindowCapture(ILogger<PrintWindowCapture>? logger = null) => _log = logger;

    /// <inheritdoc />
    /// <remarks>
    /// Returns null (never throws) for invalid, destroyed, minimised or cloaked windows, for protected content that renders black, for timeouts and
    /// when the concurrency limit is reached. Cancellation is honoured by throwing <see cref="OperationCanceledException"/>.
    /// </remarks>
    public async Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        nint hwnd = handle is > 0 and <= uint.MaxValue ? (nint)handle : 0;
        if (hwnd == 0) return null;
        int width = Math.Clamp(maxWidth, MinWidth, MaxWidth);

        if (!await _gate.WaitAsync(GateWaitMs, ct).ConfigureAwait(false))
        {
            _log?.LogDebug("window capture skipped: {Limit} captures already in flight", MaxConcurrentCaptures);
            return null;
        }

        Task<byte[]?> work;
        try
        {
            work = Task.Factory.StartNew(() => CaptureOnWorker(hwnd, width), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _gate.Release();
            _log?.LogDebug("window capture could not start a worker: {Type}", ex.GetType().Name);
            return null;
        }

        // The slot is released when the worker really finishes, even if this caller has already timed out.
        _ = work.ContinueWith(static (_, state) => ((SemaphoreSlim)state!).Release(), _gate,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            return await work.WaitAsync(TimeSpan.FromMilliseconds(PerWindowTimeoutMs), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _log?.LogDebug("window capture timed out after {Ms} ms", PerWindowTimeoutMs);
            return null;
        }
    }

    private byte[]? CaptureOnWorker(nint hwnd, int width)
    {
        try
        {
            return CaptureCore(hwnd, width);
        }
        catch (Exception ex)
        {
            _log?.LogDebug("window capture failed: {Type}", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The capture pipeline without the threading. Internal so the policy is testable where the OS allows it.</summary>
    internal static byte[]? CaptureCore(nint hwnd, int maxWidth)
    {
        if (!DeskNative.IsLiveWindow(hwnd)) return null;
        if (DeskNative.IsMinimised(hwnd)) return null;
        uint? cloaked = DeskNative.CloakedFlags(hwnd);
        if (cloaked is null || cloaked.Value != 0) return null; // unreadable or cloaked: DWM would not compose it
        if (!DeskNative.TryGetFrameBounds(hwnd, out DeskRect frame)) return null;
        if (!DeskNative.TryGetWindowRect(hwnd, out DeskRect window)) return null;
        if (!ImageMath.IsValidSource(frame.Width, frame.Height, MaxSourceEdge, MaxSourcePixels)) return null;
        if (!ImageMath.IsValidSource(window.Width, window.Height, MaxSourceEdge, MaxSourcePixels)) return null;

        // PrintWindow paints the whole window rectangle; the visible frame sits inside it, offset by the invisible resize borders.
        int offsetX = frame.Left - window.Left;
        int offsetY = frame.Top - window.Top;
        if (offsetX < 0 || offsetY < 0 || offsetX + frame.Width > window.Width || offsetY + frame.Height > window.Height) return null;

        byte[]? full = DeskGdi.PrintWindowBgra(hwnd, window.Width, window.Height);
        if (full is null) return null;
        byte[] pixels = offsetX == 0 && offsetY == 0 && window.Width == frame.Width && window.Height == frame.Height
            ? full
            : ImageMath.Crop(full, window.Width, window.Height, offsetX, offsetY, frame.Width, frame.Height);

        if (ImageMath.IsAllBlack(pixels)) return null; // protected content or a window that did not paint
        ImageMath.SetOpaque(pixels);

        var (targetWidth, targetHeight) = ImageMath.FitSize(frame.Width, frame.Height, maxWidth);
        if (targetWidth <= 0 || targetHeight <= 0) return null;
        byte[] scaled = targetWidth == frame.Width && targetHeight == frame.Height
            ? pixels
            : ImageMath.Downscale(pixels, frame.Width, frame.Height, targetWidth, targetHeight);
        return PngEncoder.Encode(scaled, targetWidth, targetHeight);
    }
}
