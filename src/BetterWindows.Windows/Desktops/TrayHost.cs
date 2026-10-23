using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: desktops stream. Replace with the real implementation.</summary>
public sealed class TrayHost : ITrayHost
{
    public bool IsSupported => false;
    public void Start() { }
    public void Stop() { }
    public IReadOnlyList<TrayIcon> Icons => Array.Empty<TrayIcon>();
    public event Action? Changed { add { } remove { } }
    public Task<byte[]?> GetIconPngAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    public void Click(string id, TrayButton button, NativePoint screenPx) { }
    public void Dispose() { }
}
