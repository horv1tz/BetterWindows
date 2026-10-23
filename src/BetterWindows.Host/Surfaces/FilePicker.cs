using System.Windows;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Win32;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Native open dialogs for the settings window (the UI cannot obtain filesystem paths itself). The dialog is modal to the owner surface's window, runs on the UI thread,
/// and only one dialog exists at a time (a second request returns empty). The returned paths are what the user picked; Core grants them for the session.
/// </summary>
public sealed class FilePicker : IFilePicker
{
    private readonly SurfaceManager _manager;
    private int _busy;

    /// <summary>Owner: host-surfaces. Resolves the owner window through the surface manager (the dispatcher and the windows live there).</summary>
    public FilePicker(SurfaceManager manager) => _manager = manager;

    public async Task<IReadOnlyList<string>> PickAsync(PickPathParams request, SurfaceId owner, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return Array.Empty<string>();
        try
        {
            var dispatcher = _manager.UiDispatcher;
            if (dispatcher is null) return Array.Empty<string>();
            var operation = dispatcher.InvokeAsync(() => ShowDialog(request, owner));
            return await operation.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Array.Empty<string>(); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Runs on the UI thread.</summary>
    private IReadOnlyList<string> ShowDialog(PickPathParams request, SurfaceId owner)
    {
        var ownerWindow = _manager.WindowOf(owner);
        var title = request.Title is { Length: > 0 } t ? t.Length > 200 ? t[..200] : t : null;
        try
        {
            if (request.Kind == PickKind.Folder)
            {
                // WPF's folder dialog (Windows Explorer style) exists in .NET 8.
                var folder = new OpenFolderDialog { Title = title ?? "", Multiselect = request.Multiple };
                if (folder.ShowDialog(ownerWindow) != true) return Array.Empty<string>();
                return request.Multiple ? folder.FolderNames : new[] { folder.FolderName };
            }

            var file = new OpenFileDialog
            {
                Title = title ?? "",
                Multiselect = request.Multiple,
                Filter = request.Kind switch
                {
                    PickKind.Image => "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|All files|*.*",
                    PickKind.App => "Applications (*.exe, *.lnk)|*.exe;*.lnk|All files|*.*",
                    _ => "All files|*.*",
                },
            };
            if (file.ShowDialog(ownerWindow) != true) return Array.Empty<string>();
            return request.Multiple ? file.FileNames : new[] { file.FileName };
        }
        catch (Exception)
        {
            // A broken shell extension must not take the settings window down with it: cancelled is the safe answer.
            return Array.Empty<string>();
        }
    }
}
