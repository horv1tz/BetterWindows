using System.Diagnostics;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

/// <summary>
/// Starts apps and opens targets through the shell (ShellExecuteEx), always as the current, non-elevated user (the verb is never "runas").
/// Everything is validated again here (<see cref="ShellTargetPolicy"/>) because the UI that ultimately triggers these calls is untrusted.
/// ShellExecuteEx runs on a dedicated STA thread: shell verbs/handlers may require an STA, and COM must be initialised before the call.
/// </summary>
public sealed class ShellAppLauncher : IAppLauncher, IDisposable
{
    // The call blocks until the activation is handed over (SEE_MASK_NOASYNC); a UAC prompt or a slow handler can take a while, but never forever.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorBadPathname = 161;

    private readonly ILogger _log;
    private readonly object _gate = new();
    private StaWorker? _sta;
    private bool _disposed;

    /// <summary>Creates the launcher.</summary>
    public ShellAppLauncher(ILogger<ShellAppLauncher>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public LaunchResult Launch(LaunchTarget target, string? arguments = null)
    {
        try
        {
            if (!ShellTargetPolicy.AreArgumentsSafe(arguments))
            {
                _log.LogWarning("Refusing launch: unsafe arguments");
                return LaunchResult.Failed;
            }
            var args = string.IsNullOrWhiteSpace(arguments) ? null : arguments;

            if (!string.IsNullOrWhiteSpace(target.Aumid))
            {
                if (!AppIdentity.IsValidAumid(target.Aumid))
                {
                    _log.LogWarning("Refusing launch: invalid AUMID");
                    return LaunchResult.Failed;
                }
                return RunSta(() => LaunchAumid(target.Aumid!.Trim(), args));
            }

            if (!string.IsNullOrWhiteSpace(target.Path))
            {
                var path = AppIdentity.NormalizeWindowsPath(target.Path);
                if (path == null)
                {
                    _log.LogWarning("Refusing launch: path is not an absolute local path");
                    return LaunchResult.Failed;
                }
                if (!File.Exists(path))
                    return Directory.Exists(path) ? RunSta(() => Execute(path, null, null)) : LaunchResult.NotFound;
                return RunSta(() => Execute(path, args, WorkingDirectoryFor(path)));
            }
            return LaunchResult.NotFound;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Launch failed");
            return LaunchResult.Failed;
        }
    }

    /// <inheritdoc />
    public bool Open(string target)
    {
        try
        {
            var kind = ShellTargetPolicy.ClassifyOpenTarget(target, out var canonical);
            if (kind == OpenTargetKind.Rejected)
            {
                _log.LogWarning("Open refused: target is not an allow-listed URL, ms-settings URI, shell literal or existing local path");
                return false;
            }
            // Files launch with their own folder as working directory (like double-clicking them); everything else has no meaningful one.
            string? workDir = kind == OpenTargetKind.File ? WorkingDirectoryFor(canonical) : null;
            return RunSta(() => Execute(canonical, null, workDir)) == LaunchResult.Started;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Open failed");
            return false;
        }
    }

    /// <inheritdoc />
    public bool RevealInExplorer(string path)
    {
        try
        {
            var normalized = AppIdentity.NormalizeWindowsPath(path);
            if (normalized == null || (!File.Exists(normalized) && !Directory.Exists(normalized)))
            {
                _log.LogWarning("Reveal refused: not an existing absolute local path");
                return false;
            }
            if (RunSta(() => SelectInExplorer(normalized))) return true;
            return RevealWithExplorerExe(normalized);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reveal failed");
            return false;
        }
    }

    // ---------------------------------------------------------------- implementation

    /// <summary>
    /// The folder of the file, for programs. Shortcuts (.lnk/.url) carry their own "Start in" folder, which must win, and documents are opened by their handler, so no override there.
    /// </summary>
    internal static string? WorkingDirectoryFor(string path)
        => AppIdentity.ExtensionOf(path) is ".exe" or ".bat" or ".cmd" or ".com" or ".msc" or ".cpl" ? AppIdentity.DirectoryOf(path) : null;

    private LaunchResult LaunchAumid(string aumid, string? args)
    {
        var parsing = AppIdentity.AppsFolderParsingName(aumid);
        // Existence first: ShellExecute on a missing AppsFolder child would show an error UI or fail with a generic code.
        var item = AppsCom.CreateShellItem(parsing, out int hr);
        if (item == null)
        {
            _log.LogInformation("AppsFolder item not found for the requested AUMID (0x{Hr:X8})", hr);
            return LaunchResult.NotFound;
        }
        AppsCom.Release(item);
        return Execute(parsing, args, null);
    }

    /// <summary>
    /// ShellExecuteEx(lpVerb = NULL -> the default verb, never "runas"). Flags: SEE_MASK_NOASYNC (do not return before the activation has been handed over: the calling
    /// thread is a long-lived STA but the documentation requires it for handlers that continue on a background thread), SEE_MASK_FLAG_NO_UI (no shell error dialogs, we report the result),
    /// SEE_MASK_DOENVSUBST is deliberately NOT set (no environment expansion of untrusted strings).
    /// </summary>
    private LaunchResult Execute(string file, string? parameters, string? workingDirectory)
    {
        unsafe
        {
            fixed (char* pFile = file)
            fixed (char* pParams = parameters)
            fixed (char* pDir = workingDirectory)
            {
                SHELLEXECUTEINFOW info = default;
                info.cbSize = (uint)sizeof(SHELLEXECUTEINFOW);
                info.fMask = 0x00000100 /* SEE_MASK_NOASYNC */ | 0x00000400 /* SEE_MASK_FLAG_NO_UI */;
                info.lpFile = new PCWSTR(pFile);
                info.lpParameters = parameters == null ? default : new PCWSTR(pParams);
                info.lpDirectory = workingDirectory == null ? default : new PCWSTR(pDir);
                info.nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL;

                if (PInvoke.ShellExecuteEx(ref info)) return LaunchResult.Started;
                // CsWin32 does not set SetLastError on this import, so read the thread's current system error directly (immediately, before anything else can overwrite it).
                int error = System.Runtime.InteropServices.Marshal.GetLastSystemError();
                // hInstApp carries the legacy SE_ERR_* code on failure (2 = file not found, 3 = path not found).
                int legacy = (int)(long)(nint)info.hInstApp.Value;
                _log.LogInformation("ShellExecuteEx failed (Win32 error {Error}, legacy code {Legacy})", error, legacy);
                bool notFound = error is ErrorFileNotFound or ErrorPathNotFound or ErrorBadPathname || legacy is 2 or 3;
                return notFound ? LaunchResult.NotFound : LaunchResult.Failed;
            }
        }
    }

    /// <summary>SHParseDisplayName + SHOpenFolderAndSelectItems (documented way to open a folder window with an item selected). False on any failure.</summary>
    private bool SelectInExplorer(string path)
    {
        int hr = AppsShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        if (hr < 0 || pidl == IntPtr.Zero)
        {
            _log.LogDebug("SHParseDisplayName failed: 0x{Hr:X8}", hr);
            return false;
        }
        try
        {
            // cidl = 0 with the full item PIDL as the "folder": the shell selects that item in its parent folder.
            hr = AppsShellNative.SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
            if (hr < 0) _log.LogDebug("SHOpenFolderAndSelectItems failed: 0x{Hr:X8}", hr);
            return hr >= 0;
        }
        finally
        {
            AppsShellNative.ILFree(pidl);
        }
    }

    /// <summary>Fallback: <c>explorer.exe /select,"path"</c>. The path is quoted and cannot contain quotes (NormalizeWindowsPath + file-system rules), so it cannot add switches.</summary>
    private bool RevealWithExplorerExe(string path)
    {
        try
        {
            if (path.Contains('"')) return false;
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorer = string.IsNullOrEmpty(windir) ? "explorer.exe" : System.IO.Path.Combine(windir, "explorer.exe");
            using var p = Process.Start(new ProcessStartInfo(explorer) { Arguments = $"/select,\"{path}\"", UseShellExecute = false });
            return p != null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "explorer.exe /select fallback failed");
            return false;
        }
    }

    private T RunSta<T>(Func<T> work)
    {
        StaWorker sta;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sta = _sta ??= new StaWorker("BW.Launcher.STA");
        }
        var task = sta.RunAsync(work);
        if (!task.Wait(CallTimeout))
        {
            _log.LogWarning("Shell call did not return within {Seconds}s", CallTimeout.TotalSeconds);
            // The STA thread is stuck inside the shell. Retire it so later launches get a fresh thread.
            lock (_gate)
            {
                if (ReferenceEquals(_sta, sta)) _sta = null;
            }
            sta.Dispose();
            throw new TimeoutException("Shell call timed out");
        }
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Releases the STA thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sta?.Dispose();
            _sta = null;
        }
    }
}
