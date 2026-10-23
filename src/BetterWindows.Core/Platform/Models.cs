namespace BetterWindows.Core.Platform;

/// <summary>Rectangle in physical (device) pixels, Win32 RECT semantics (Right/Bottom exclusive).</summary>
public readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public readonly record struct NativePoint(int X, int Y);

/// <summary>Everything the platform can tell us about one top-level window. Pure data: no behaviour.</summary>
public sealed record RawWindow
{
    public long Handle { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    /// <summary>Full path of the owning process image, null if inaccessible (elevated/protected process).</summary>
    public string? ExePath { get; init; }
    // NOTE (UWP/packaged windows): for class ApplicationFrameWindow the Windows layer reports the HOSTED app: ProcessId/ExePath/Aumid come from the visible child
    // Windows.UI.Core.CoreWindow whose process differs from the frame (GetApplicationUserModelId on that process). With no CoreWindow child (minimised/suspended) it
    // uses the frame's SHGetPropertyStoreForWindow AUMID and leaves ExePath null. It NEVER reports ApplicationFrameHost.exe as the app; ClassName stays
    // "ApplicationFrameWindow". An explicit per-window AUMID (property store) wins over the process AUMID.
    /// <summary>Explicit AppUserModelID of the window (property store) or of the packaged process; null if none.</summary>
    public string? Aumid { get; init; }
    public bool IsVisible { get; init; }
    /// <summary>DWMWA_CLOAKED != 0 (UWP suspended windows, windows on other virtual desktops). Equals CloakedFlags != 0.</summary>
    public bool IsCloaked { get; init; }
    /// <summary>Raw DWMWA_CLOAKED mask: DWM_CLOAKED_APP=1, DWM_CLOAKED_SHELL=2, DWM_CLOAKED_INHERITED=4; 0 when not cloaked.</summary>
    public uint CloakedFlags { get; init; }
    /// <summary>
    /// Cloaked because it lives on ANOTHER virtual desktop (DWM_CLOAKED_SHELL). Such windows are real, running windows:
    /// they must still appear in the dock's running state and in Mission Control (with their desktop id), unlike windows
    /// cloaked by the app itself (suspended UWP, DWM_CLOAKED_APP), which are hidden.
    /// </summary>
    public bool IsCloakedByShell { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }
    /// <summary>WS_EX_TOOLWINDOW.</summary>
    public bool IsToolWindow { get; init; }
    /// <summary>WS_EX_APPWINDOW.</summary>
    public bool IsAppWindow { get; init; }
    /// <summary>WS_EX_NOACTIVATE.</summary>
    public bool IsNoActivate { get; init; }
    /// <summary>GetWindow(GW_OWNER) != 0.</summary>
    public long OwnerHandle { get; init; }
    /// <summary>
    /// The owning process runs elevated (or is otherwise not queryable/controllable from this non-elevated process): activate/minimise/close
    /// are subject to UIPI and may silently fail. Best effort: set when the process token reports elevation, or when opening the process with
    /// PROCESS_QUERY_LIMITED_INFORMATION succeeds but the token query is denied.
    /// </summary>
    public bool IsElevated { get; init; }
    /// <summary>The process is BetterWindows itself.</summary>
    public bool IsOwnProcess { get; init; }
    /// <summary>Taskbar-button flash requested (FlashWindowEx / HSHELL_FLASH).</summary>
    public bool IsFlashing { get; init; }
    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS (the VISIBLE frame) in virtual-screen physical px, falling back to GetWindowRect.</summary>
    public NativeRect Bounds { get; init; }
}

public enum WindowEventKind
{
    Created, Destroyed, Shown, Hidden, TitleChanged, Foreground, Minimized, Restored, Cloaked, Uncloaked, Moved, Flashed, Redraw,
}

/// <summary>A change notification; consumers re-describe the handle via <see cref="IWindowSystem.TryDescribe"/>.</summary>
public readonly record struct WindowEvent(WindowEventKind Kind, long Handle);

public enum LaunchResult { Started, Failed, NotFound }

/// <summary>Identifies something that has an icon. Exactly one of the identifying members is used, in this order.</summary>
public sealed record IconKey
{
    public string? Aumid { get; init; }
    public string? Path { get; init; }
    public long? WindowHandle { get; init; }
}

public readonly record struct LaunchTarget(string? Aumid, string? Path)
{
    public static LaunchTarget FromAumid(string aumid) => new(aumid, null);
    public static LaunchTarget FromPath(string path) => new(null, path);
}

public readonly record struct FileHit(string Path, string Name, bool IsFolder, DateTimeOffset? Modified);

/// <summary>Keyboard gesture parsed from "Ctrl+Alt+Q" etc. Modifiers use the Win32 MOD_* bit values.</summary>
public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;
}

public enum AppBarEdge { Left, Top, Right, Bottom }

public enum AppBarNotification { PosChanged, FullScreenAppEntered, FullScreenAppLeft, WindowArrange }
