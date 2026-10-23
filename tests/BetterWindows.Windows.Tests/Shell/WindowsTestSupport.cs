using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Name of the xUnit collection that serialises every test touching global desktop state (work area, taskbar, foreground, hotkeys).</summary>
public static class ShellCollection
{
    /// <summary>Collection name.</summary>
    public const string Name = "win-shell-global-state";
}

/// <summary>Disables parallelism between the Windows integration tests of the shell stream (they share the desktop).</summary>
[CollectionDefinition(ShellCollection.Name, DisableParallelization = true)]
public class ShellCollectionDefinition
{
}

/// <summary>Polling helpers: Windows state changes (work area, visibility, events) arrive asynchronously.</summary>
internal static class Poll
{
    /// <summary>Waits until the condition is true; returns whether it became true within the timeout.</summary>
    public static bool Until(Func<bool> condition, int timeoutMs = 5000, int stepMs = 50)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                return false;
            }

            Thread.Sleep(stepMs);
        }
    }
}

/// <summary>Minimal user32 declarations for the test helpers (the test project is compiled without unsafe code).</summary>
internal static class TestNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] public static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] public static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint GetDesktopWindow();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern nint GetConsoleWindow();
}

/// <summary>
/// A real top-level window (class STATIC, popup, tool window, shown without activation) owned by a dedicated STA thread with its own message
/// loop, for tests that need an HWND with a known title. Destroyed on dispose.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsBorder = 0x00800000;
    private const uint WsExToolWindow = 0x00000080;
    private const int SwShowNa = 8;
    private const uint WmClose = 0x0010;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private nint _handle;

    private TestWindow(string title, bool visible, int x, int y, int width, int height)
    {
        _thread = new Thread(() => Run(title, visible, x, y, width, height)) { IsBackground = true, Name = "TestWindow" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(10));
    }

    /// <summary>The HWND (0 if creation failed).</summary>
    public nint Handle => _handle;

    /// <summary>Creates the window; the caller disposes it.</summary>
    public static TestWindow Create(string title, bool visible = true, int x = 60, int y = 60, int width = 260, int height = 140) =>
        new(title, visible, x, y, width, height);

    private void Run(string title, bool visible, int x, int y, int width, int height)
    {
        try
        {
            nint hwnd = TestNative.CreateWindowEx(WsExToolWindow, "STATIC", title, WsPopup | WsBorder, x, y, width, height, 0, 0, 0, 0);
            if (hwnd == 0)
            {
                return;
            }

            _handle = hwnd;
            if (visible)
            {
                TestNative.ShowWindow(hwnd, SwShowNa); // visible, but never steals the foreground
            }

            _ready.Set();
            while (TestNative.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TestNative.TranslateMessage(ref msg);
                TestNative.DispatchMessage(ref msg);
                if (!TestNative.IsWindow(hwnd))
                {
                    break;
                }
            }
        }
        finally
        {
            _ready.Set();
        }
    }

    /// <summary>Posts WM_CLOSE (the STATIC class forwards it to DefWindowProc which destroys the window) and joins the thread.</summary>
    public void Dispose()
    {
        if (_handle != 0)
        {
            TestNative.PostMessage(_handle, WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }
}

/// <summary>Starts classic Notepad as a foreign-process window for event tests; skip-friendly (null when it cannot be launched).</summary>
internal sealed class ExternalWindow : IDisposable
{
    private ExternalWindow(long handle, int pid)
    {
        Handle = handle;
        ProcessId = pid;
    }

    /// <summary>HWND of the Notepad main window.</summary>
    public long Handle { get; }

    /// <summary>Owning process.</summary>
    public int ProcessId { get; }

    /// <summary>Launches Notepad and waits (bounded) until its window shows up in the enumeration. Null if not possible on this machine.</summary>
    public static ExternalWindow? TryLaunchNotepad(Win32WindowSystem ws)
    {
        var before = ws.EnumerateTopLevel().Select(w => w.Handle).ToHashSet();
        Process? launched = null;
        try
        {
            launched = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            launched?.Dispose();
        }

        Core.Platform.RawWindow? found = null;
        Poll.Until(() =>
        {
            found = ws.EnumerateTopLevel().FirstOrDefault(w =>
                !before.Contains(w.Handle) && w.IsVisible && !w.IsOwnProcess &&
                (w.ClassName == "Notepad" || w.ExePath?.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase) == true));
            return found != null;
        }, 15000, 200);
        return found == null ? null : new ExternalWindow(found.Handle, found.ProcessId);
    }

    /// <summary>Closes the window and kills the process if it does not go away.</summary>
    public void Dispose()
    {
        try
        {
            using var p = Process.GetProcessById(ProcessId);
            p.CloseMainWindow();
            if (!p.WaitForExit(2000))
            {
                p.Kill();
                p.WaitForExit(2000);
            }
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}
