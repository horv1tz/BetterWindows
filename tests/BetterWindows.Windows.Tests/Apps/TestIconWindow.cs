using System.Runtime.InteropServices;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>
/// A tiny never-shown window with an icon, living on its own message-loop thread (WM_GETICON is answered by the thread that owns the window, so that
/// thread must pump). Hand-declared P/Invokes: the test project has no unsafe code / CsWin32 of its own.
/// </summary>
internal sealed class TestIconWindow : IDisposable
{
    private const uint WmSetIcon = 0x0080;
    private const uint WmQuit = 0x0012;
    private const nint IconBig = 1;
    private const nint IdiApplication = 32512;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessageW(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint thread, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;

    public long Handle { get; private set; }

    public TestIconWindow(bool withIcon)
    {
        _thread = new Thread(() => Run(withIcon)) { IsBackground = true, Name = "TestIconWindow" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        Handle.Should().NotBe(0, "the helper window must have been created");
    }

    private void Run(bool withIcon)
    {
        _threadId = GetCurrentThreadId();
        // "Static" is a predefined control class: no class registration needed. WS_POPUP, never shown.
        var hwnd = CreateWindowExW(0, "Static", "bw-test-icon-window", 0x80000000, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Handle = hwnd.ToInt64();
        if (hwnd != IntPtr.Zero && withIcon)
        {
            var icon = LoadIconW(IntPtr.Zero, IdiApplication); // shared system icon: must not be destroyed
            SendMessageW(hwnd, WmSetIcon, IconBig, icon);
        }
        _ready.Set();
        if (hwnd == IntPtr.Zero) return;
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        DestroyWindow(hwnd);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
