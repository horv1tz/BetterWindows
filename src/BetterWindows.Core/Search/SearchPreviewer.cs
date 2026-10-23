using System.Text;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search;

/// <summary>
/// Lazy previews for the selected result (search.preview). Text files: the first 4 KB when the file looks like text (BOM detection; no NUL bytes) and is at most
/// 2 MB. Images: the thumbnail through the path icon source at 256 px. Apps: their full path. Everything else, and every file that is missing, too large,
/// binary, a folder or a reparse point (symlink/junction: never followed), gives kind none.
/// </summary>
public sealed class SearchPreviewer
{
    public const int MaxTextBytes = 4096;
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxTextChars = 4096;
    public const int ImageSize = 256;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>The preview for a resolved hit. Runs file IO on the thread pool; never throws.</summary>
    public Task<SearchPreview> PreviewAsync(SearchHit hit, CancellationToken ct)
    {
        if (hit.Preview is not null) return Task.FromResult(hit.Preview);
        if (hit.PreviewPath is null) return Task.FromResult(NoPreview);
        var path = hit.PreviewPath;
        var isApp = hit.PreviewIsApp;
        return Task.Run(() => Build(path, isApp, ct), ct);
    }

    internal static readonly SearchPreview NoPreview = new() { Kind = SearchPreviewKind.None };

    internal static SearchPreview Build(string rawPath, bool isApp, CancellationToken ct)
    {
        try
        {
            if (!InputPolicy.TryNormalizeLocalPath(rawPath, out var path)) return NoPreview;
            if (isApp) return TextPreview(path);
            if (ct.IsCancellationRequested) return NoPreview;

            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext) && File.Exists(path))
            {
                return new SearchPreview { Kind = SearchPreviewKind.Image, ImageUrl = DynUrls.PathIcon(path, ImageSize) };
            }
            return TextFilePreview(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return NoPreview;
        }
    }

    private static SearchPreview TextPreview(string text) => new() { Kind = SearchPreviewKind.Text, Text = InputPolicy.Truncate(text, MaxTextChars) };

    private static SearchPreview TextFilePreview(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return NoPreview;
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return NoPreview;
        if (info.Length == 0 || info.Length > MaxFileBytes) return NoPreview;

        byte[] head = new byte[(int)Math.Min(MaxTextBytes, info.Length)];
        int read;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
        {
            int total = 0;
            while (total < head.Length && (read = stream.Read(head, total, head.Length - total)) > 0)
            {
                if (ct.IsCancellationRequested) return NoPreview;
                total += read;
            }
            if (total < head.Length) Array.Resize(ref head, total);
        }

        var text = DecodeIfText(head);
        return text is null ? NoPreview : TextPreview(text);
    }

    /// <summary>Decodes the head of a file if it looks like text: BOM-marked UTF-8/UTF-16, or bytes without NULs (UTF-8). Null for binary data.</summary>
    internal static string? DecodeIfText(byte[] head)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return Strip(new UTF8Encoding(false).GetString(head, 3, head.Length - 3));
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE) return Strip(Encoding.Unicode.GetString(head, 2, head.Length - 2));
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF) return Strip(Encoding.BigEndianUnicode.GetString(head, 2, head.Length - 2));
        if (Array.IndexOf(head, (byte)0) >= 0) return null;
        return Strip(new UTF8Encoding(false, false).GetString(head));
    }

    /// <summary>Drops control characters except tabs and line breaks, and a partial last character cut by the byte limit.</summary>
    private static string Strip(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '�') continue;
            sb.Append(c < 0x20 && c is not ('\t' or '\n' or '\r') ? ' ' : c);
        }
        return sb.ToString();
    }
}
