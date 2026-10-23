using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>STUB — owner: host-surfaces. Native open dialog (Microsoft.Win32.OpenFileDialog / OpenFolderDialog) owned by the surface's window, on the UI thread.</summary>
public sealed class FilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickAsync(PickPathParams request, SurfaceId owner, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
