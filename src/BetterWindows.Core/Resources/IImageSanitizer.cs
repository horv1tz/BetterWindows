namespace BetterWindows.Core.Resources;

/// <summary>Bounds for <see cref="IImageSanitizer"/>: cover art is at most 1024 px / 2 MB, wallpapers at most 4096 px / 64 MB.</summary>
public sealed record ImageLimits(int MaxLongEdgePx, int MaxBytes)
{
    public static readonly ImageLimits CoverArt = new(1024, 2 * 1024 * 1024);
    public static readonly ImageLimits Wallpaper = new(4096, 64 * 1024 * 1024);
}

/// <summary>
/// OPTIONAL port: decodes untrusted image bytes (cover art controlled by media apps, user-chosen wallpapers) and RE-ENCODES them (PNG/JPEG) bounded by
/// <see cref="ImageLimits"/>, so a crafted file never reaches the renderer's decoders unchanged (docs/IPC.md section 8). Core has no image decoder, so the real implementation
/// lives in the Windows layer (WIC / System.Drawing). Without one, <see cref="PassThroughImageSanitizer"/> is used. Returns null if the bytes are not an acceptable image.
/// </summary>
public interface IImageSanitizer
{
    Task<SanitizedImage?> SanitizeAsync(byte[] data, ImageLimits limits, CancellationToken ct = default);
}

/// <summary>Image bytes that are safe to serve, with their (sniffed) content type.</summary>
public sealed record SanitizedImage(byte[] Data, string ContentType);

/// <summary>
/// Default sanitizer when the Windows layer provides none: it cannot decode, so it only lets through bytes that START like a PNG or JPEG and fit the byte limit. It does NOT
/// enforce the pixel limit (that needs a decoder). BMP/WebP/GIF are refused here and need a real <see cref="IImageSanitizer"/>.
/// </summary>
public sealed class PassThroughImageSanitizer : IImageSanitizer
{
    public Task<SanitizedImage?> SanitizeAsync(byte[] data, ImageLimits limits, CancellationToken ct = default)
    {
        if (data.Length == 0 || data.Length > limits.MaxBytes) return Task.FromResult<SanitizedImage?>(null);
        var type = ImageFormats.Sniff(data);
        return Task.FromResult(type is ImageFormats.Png or ImageFormats.Jpeg ? new SanitizedImage(data, type) : null);
    }
}
