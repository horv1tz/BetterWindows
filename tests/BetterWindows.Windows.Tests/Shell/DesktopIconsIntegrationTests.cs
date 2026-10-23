using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>
/// Real desktop icon tests. They briefly hide Explorer's icon list, so they run in the shell collection (no parallel shell tests) and
/// always restore it. Skipped when Explorer's desktop is not running (headless CI images).
/// </summary>
[Collection(ShellCollection.Name)]
public class DesktopIconsIntegrationTests
{
    private static void RequireDesktop()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.If(DesktopShellNative.FindListView() == 0, "Explorer's desktop icon list is not running on this session");
    }

    private static bool IconsVisible()
    {
        nint lv = DesktopShellNative.FindListView();
        return lv != 0 && TestNative.IsWindowVisible(lv);
    }

    [SkippableFact]
    public void Hide_then_Dispose_leaves_the_icon_list_visible_and_removes_the_marker()
    {
        RequireDesktop();
        bool wasVisible = IconsVisible();
        Skip.IfNot(wasVisible, "the desktop icons are already hidden by the user");

        using (var controller = new Win32DesktopIconsController(NullLogger<Win32DesktopIconsController>.Instance))
        {
            controller.SetNativeIconsVisible(false);
            Poll.Until(() => !IconsVisible()).Should().BeTrue("the icon list should hide");
            ShellStateStore.Exists(DesktopIconsMarker.FileName).Should().BeTrue("the hide must be recorded before it is applied");
        }

        Poll.Until(IconsVisible).Should().BeTrue("Dispose restores the icons");
        ShellStateStore.Exists(DesktopIconsMarker.FileName).Should().BeFalse();
    }

    [SkippableFact]
    public void RestoreNow_is_idempotent_and_never_throws_without_a_marker()
    {
        RequireDesktop();
        bool wasVisible = IconsVisible();

        DesktopIconsRestorer.RestoreNow();
        DesktopIconsRestorer.RestoreNow();

        IconsVisible().Should().Be(wasVisible, "without a marker the restorer must not touch the user's icons");
    }

    [SkippableFact]
    public void RestoreNow_undoes_a_hide_left_behind_by_a_crashed_run()
    {
        RequireDesktop();
        Skip.IfNot(IconsVisible(), "the desktop icons are already hidden by the user");

        // Simulate the crash: hide through the marker-writing path, then forget the controller without restoring.
        var controller = new Win32DesktopIconsController(NullLogger<Win32DesktopIconsController>.Instance);
        try
        {
            controller.SetNativeIconsVisible(false);
            Poll.Until(() => !IconsVisible()).Should().BeTrue();
            DesktopIconsRestorer.RestoreNow();
            // The restore uses ShowWindowAsync (a hung Explorer must not block the crash path), so Explorer applies it on its own thread.
            Poll.Until(IconsVisible).Should().BeTrue("the icons come back once Explorer handles the asynchronous show request");
            ShellStateStore.Exists(DesktopIconsMarker.FileName).Should().BeFalse();
        }
        finally
        {
            controller.Dispose();
        }
    }

    [SkippableFact]
    public void TryAttachDesktopLayer_on_an_invalid_window_returns_false_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var controller = new Win32DesktopIconsController(NullLogger<Win32DesktopIconsController>.Instance);

        controller.TryAttachDesktopLayer(0, new NativeRect(0, 0, 100, 100)).Should().BeFalse();
        controller.TryAttachDesktopLayer(1, new NativeRect(0, 0, 0, 0)).Should().BeFalse();
        controller.DetachDesktopLayer(1); // not attached: a no-op
    }
}
