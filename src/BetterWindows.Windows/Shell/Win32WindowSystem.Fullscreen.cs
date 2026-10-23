using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace BetterWindows.Native;

public sealed unsafe partial class Win32WindowSystem
{
    /// <inheritdoc />
    public bool IsFullscreenWindowActive(MonitorInfo monitor)
    {
        try
        {
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            HWND fg = PInvoke.GetForegroundWindow();
            if (fg.IsNull)
            {
                return false;
            }

            int pid = WindowNative.GetProcessId(fg, out uint threadId);
            if (threadId == 0 || pid == Environment.ProcessId)
            {
                return false;
            }

            string className = WindowNative.GetClassName(fg);
            if (FullscreenRules.IsExcludedClass(className) || !WindowNative.TryGetFrameBounds(fg, out var frame))
            {
                return false;
            }

            uint style = WindowNative.GetStyle(fg);
            var facts = new FullscreenFacts(
                className,
                IsOwnProcess: false,
                IsVisible: PInvoke.IsWindowVisible(fg),
                IsCloaked: WindowNative.GetCloakedFlags(fg) != 0,
                IsMinimized: (style & Win32Consts.WS_MINIMIZE) != 0,
                IsMaximized: (style & Win32Consts.WS_MAXIMIZE) != 0,
                Style: style,
                ExStyle: WindowNative.GetExStyle(fg),
                FrameBounds: frame);

            var bounds = RectMath.FromProtocol(monitor.BoundsPx);
            if (FullscreenRules.Evaluate(facts, bounds, FullscreenHint.None))
            {
                return true;
            }

            // Only ask the shell when it could change the answer (the frame is within a few pixels of covering the monitor).
            return FullscreenRules.IsNearMiss(facts, bounds)
                && QueryNotificationHint() == FullscreenHint.FullscreenApp
                && FullscreenRules.Evaluate(facts, bounds, FullscreenHint.FullscreenApp);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IsFullscreenWindowActive failed");
            return false;
        }
    }

    /// <summary>SHQueryUserNotificationState: busy / D3D full screen / presentation mode / UWP full-screen app are positive hints.</summary>
    private static FullscreenHint QueryNotificationHint()
    {
        QUERY_USER_NOTIFICATION_STATE state;
        if (PInvoke.SHQueryUserNotificationState(&state).Failed)
        {
            return FullscreenHint.None;
        }

        return state switch
        {
            QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY => FullscreenHint.FullscreenApp,
            QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => FullscreenHint.FullscreenApp,
            QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => FullscreenHint.FullscreenApp,
            QUERY_USER_NOTIFICATION_STATE.QUNS_APP => FullscreenHint.FullscreenApp,
            _ => FullscreenHint.None,
        };
    }
}
