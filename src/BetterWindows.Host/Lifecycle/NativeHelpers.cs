using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Console;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Thin, exception-free wrappers over the few Win32 calls the lifecycle code needs before WPF/WebView2 exist (CsWin32, see NativeMethods.txt).
/// Every method degrades to a safe answer instead of throwing: a failing probe must never stop the shell from starting or restoring.
/// </summary>
internal static class NativeHelpers
{
    private const string TrayClass = "Shell_TrayWnd";

    /// <summary>
    /// Standard output for a WinExe. If the parent redirected stdout (pipe/file, e.g. <c>Start-Process -RedirectStandardOutput</c> or <c>| Tee-Object</c>) the inherited handle is
    /// used; otherwise the parent's console is attached (<c>AttachConsole(ATTACH_PARENT_PROCESS)</c>) and <c>CONOUT$</c> opened, because a GUI-subsystem process has no
    /// usable standard handles even after AttachConsole. Returns <see cref="TextWriter.Null"/> when there is nowhere to write.
    /// </summary>
    public static TextWriter OpenConsoleWriter()
    {
        var utf8 = new UTF8Encoding(false);
        try
        {
            if (HasUsableStdOut()) return new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            if (!PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS)) return TextWriter.Null;
            unsafe
            {
                // GENERIC_WRITE, share write, OPEN_EXISTING: the console screen buffer of the attached console.
                var handle = PInvoke.CreateFile("CONOUT$", 0x40000000u, FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_READ, null,
                    FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
                if (handle.IsInvalid) return TextWriter.Null;
                return new StreamWriter(new FileStream(handle, FileAccess.Write, 1, false), utf8) { AutoFlush = true };
            }
        }
        catch (Exception) { return TextWriter.Null; }
    }

    private static unsafe bool HasUsableStdOut()
    {
        try
        {
            var h = PInvoke.GetStdHandle(STD_HANDLE.STD_OUTPUT_HANDLE);
            if (h == HANDLE.Null || (nint)h.Value == -1) return false;
            // FILE_TYPE_UNKNOWN (0) also covers a stale handle.
            return PInvoke.GetFileType(h) != FILE_TYPE.FILE_TYPE_UNKNOWN;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// True when this process runs with a full (elevated) token. The shell must never run elevated: UIPI would make it fight the non-elevated user session and every
    /// restore path would have to be repeated at two integrity levels (docs/IPC.md section 15, rule 2).
    /// </summary>
    public static bool IsProcessElevated()
    {
        try
        {
            unsafe
            {
                HANDLE token = default;
                if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_QUERY, &token)) return false;
                try
                {
                    TOKEN_ELEVATION elevation = default;
                    uint returned = 0;
                    // TokenElevation: documented since Vista; TokenIsElevated != 0 means a full (elevated) token.
                    if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), &returned)) return false;
                    return elevation.TokenIsElevated != 0;
                }
                finally { PInvoke.CloseHandle(token); }
            }
        }
        catch (Exception) { return false; }
    }

    /// <summary>The Explorer taskbar window (null/zero when Explorer is not (yet) running).</summary>
    public static nint FindTrayWindow()
    {
        try { unsafe { return (nint)PInvoke.FindWindow(TrayClass, null).Value; } }
        catch (Exception) { return 0; }
    }

    /// <summary>Pid of the process owning <c>Shell_TrayWnd</c> (Explorer), or 0.</summary>
    public static int FindExplorerPid()
    {
        try
        {
            var hwnd = FindTrayWindow();
            if (hwnd == 0) return 0;
            uint pid = 0;
            unsafe { _ = PInvoke.GetWindowThreadProcessId(new HWND((void*)hwnd), &pid); }
            return (int)pid;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>Native modal message box usable before any UI framework exists. Returns the button id (IDYES=6, IDNO=7, IDOK=1) or 0 on failure.</summary>
    public static int ShowMessageBox(string title, string text, bool yesNo, bool error)
    {
        try
        {
            var style = (yesNo ? MESSAGEBOX_STYLE.MB_YESNO : MESSAGEBOX_STYLE.MB_OK)
                        | (error ? MESSAGEBOX_STYLE.MB_ICONERROR : MESSAGEBOX_STYLE.MB_ICONINFORMATION)
                        | MESSAGEBOX_STYLE.MB_TOPMOST | MESSAGEBOX_STYLE.MB_SETFOREGROUND;
            return (int)PInvoke.MessageBox(HWND.Null, text, title, style);
        }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// Starts <paramref name="commandLine"/> detached from the console and window-less, optionally with <paramref name="parentPid"/> as its PARENT process
    /// (PROC_THREAD_ATTRIBUTE_PARENT_PROCESS). With Explorer as parent <c>taskkill /t</c> on this process (and a Job object that kills children) does not take the child
    /// with it. Falls back to a plain child when the parent cannot be opened. Returns the new pid or 0.
    /// </summary>
    public static int StartDetached(string exePath, string commandLine, int parentPid)
    {
        if (parentPid > 0)
        {
            var pid = TryCreateProcessWithParent(exePath, commandLine, parentPid);
            if (pid > 0) return pid;
        }
        try
        {
            var psi = new ProcessStartInfo(exePath) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in SplitArguments(commandLine)) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            return p?.Id ?? 0;
        }
        catch (Exception) { return 0; }
    }

    // PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = ProcThreadAttributeValue(ProcThreadAttributeParentProcess(0), thread: FALSE, input: TRUE, additive: FALSE) = 0x00020000 (not in the metadata).
    private const nuint ParentProcessAttribute = 0x00020000;

    private static unsafe int TryCreateProcessWithParent(string exePath, string commandLine, int parentPid)
    {
        HANDLE parent = default;
        void* attrBuffer = null;
        var attrInitialized = false;
        var list = default(LPPROC_THREAD_ATTRIBUTE_LIST);
        try
        {
            // PROCESS_CREATE_PROCESS is the only right the PARENT_PROCESS attribute needs.
            parent = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_PROCESS, false, (uint)parentPid);
            if (parent == HANDLE.Null) return 0;

            nuint size = 0;
            // The first call only reports the required size (it is documented to fail with ERROR_INSUFFICIENT_BUFFER).
            PInvoke.InitializeProcThreadAttributeList(default, 1, 0, &size);
            if (size == 0) return 0;
            attrBuffer = NativeMemory.Alloc(size);
            list = new LPPROC_THREAD_ATTRIBUTE_LIST(attrBuffer);
            if (!PInvoke.InitializeProcThreadAttributeList(list, 1, 0, &size)) return 0;
            attrInitialized = true;

            var parentHandle = parent.Value;
            if (!PInvoke.UpdateProcThreadAttribute(list, 0, ParentProcessAttribute, &parentHandle, (nuint)sizeof(void*), null, (nuint*)null)) return 0;

            var si = new STARTUPINFOEXW();
            si.StartupInfo.cb = (uint)sizeof(STARTUPINFOEXW);
            si.lpAttributeList = list;
            var cmd = new char[commandLine.Length + 1]; // CreateProcessW may modify the command line buffer: it must be writable and NUL terminated
            commandLine.CopyTo(0, cmd, 0, commandLine.Length);
            const PROCESS_CREATION_FLAGS flags = PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT | PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW
                | PROCESS_CREATION_FLAGS.CREATE_NEW_PROCESS_GROUP | PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT;
            fixed (char* pExe = exePath)
            fixed (char* pCmd = cmd)
            {
                PROCESS_INFORMATION pi;
                // A STARTUPINFOEXW is passed as a STARTUPINFOW* (the cb member tells CreateProcess it is the extended structure).
                if (!PInvoke.CreateProcess(new PCWSTR(pExe), new PWSTR(pCmd), null, null, false, flags, null, default, (STARTUPINFOW*)&si, &pi)) return 0;
                var pid = (int)pi.dwProcessId;
                PInvoke.CloseHandle(pi.hThread);
                PInvoke.CloseHandle(pi.hProcess);
                return pid;
            }
        }
        catch (Exception) { return 0; }
        finally
        {
            if (attrInitialized) PInvoke.DeleteProcThreadAttributeList(list);
            if (attrBuffer is not null) NativeMemory.Free(attrBuffer);
            if (parent != HANDLE.Null) PInvoke.CloseHandle(parent);
        }
    }

    /// <summary>Windows command-line splitting for the fallback path (our own command lines only: quoted tokens and plain tokens).</summary>
    internal static IEnumerable<string> SplitArguments(string commandLine)
    {
        var cur = new StringBuilder();
        var inQuote = false;
        var any = false;
        var first = true;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuote = !inQuote; any = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (any) { if (!first) yield return cur.ToString(); first = false; cur.Clear(); any = false; }
                continue;
            }
            cur.Append(c); any = true;
        }
        if (any && !first) yield return cur.ToString();
    }

    /// <summary>True if <paramref name="ex"/> is "access denied" (the process exists but we may not inspect it).</summary>
    public static bool IsAccessDenied(Exception ex) => ex is Win32Exception { NativeErrorCode: 5 };
}
