using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace BetterWindows.Native.Interop;

/// <summary>One synthesized keyboard event.</summary>
/// <param name="VirtualKey">Win32 virtual-key code.</param>
/// <param name="KeyUp">True for a key release.</param>
/// <param name="Extended">True for keys that need KEYEVENTF_EXTENDEDKEY (arrows, Win keys, right Ctrl/Alt, Insert/Delete/Home/End/Page keys).</param>
internal readonly record struct KeyStep(ushort VirtualKey, bool KeyUp, bool Extended);

/// <summary>SendInput plumbing shared by the input simulator and the foreground-lock workaround.</summary>
internal static unsafe class KeyboardInput
{
    /// <summary>True for keys whose hardware scan code carries the E0 prefix and therefore need KEYEVENTF_EXTENDEDKEY.</summary>
    public static bool IsExtended(ushort vk) => vk is
        Win32Consts.VK_LEFT or Win32Consts.VK_UP or Win32Consts.VK_RIGHT or Win32Consts.VK_DOWN
        or Win32Consts.VK_LWIN or Win32Consts.VK_RWIN or Win32Consts.VK_RCONTROL or Win32Consts.VK_RMENU
        or 0x21 or 0x22 or 0x23 or 0x24 or 0x2D or 0x2E; // PageUp, PageDown, End, Home, Insert, Delete

    /// <summary>Convenience constructor that fills the extended flag.</summary>
    public static KeyStep Down(ushort vk) => new(vk, false, IsExtended(vk));

    /// <summary>Convenience constructor that fills the extended flag.</summary>
    public static KeyStep Up(ushort vk) => new(vk, true, IsExtended(vk));

    /// <summary>
    /// Injects the steps as ONE SendInput call (the system guarantees they are not interleaved with other input).
    /// Returns the number of events the system accepted (less than requested when blocked, e.g. by UIPI).
    /// </summary>
    public static uint Send(ReadOnlySpan<KeyStep> steps)
    {
        if (steps.IsEmpty)
        {
            return 0;
        }

        var inputs = new INPUT[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            var flags = default(KEYBD_EVENT_FLAGS);
            if (s.Extended)
            {
                flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
            }

            if (s.KeyUp)
            {
                flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
            }

            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].Anonymous.ki = new KEYBDINPUT
            {
                wVk = (VIRTUAL_KEY)s.VirtualKey,
                // The scan code makes the event look like real hardware input to apps that read it instead of the VK.
                wScan = (ushort)PInvoke.MapVirtualKey(s.VirtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC),
                dwFlags = flags,
            };
        }

        fixed (INPUT* p = inputs)
        {
            return PInvoke.SendInput((uint)inputs.Length, p, sizeof(INPUT));
        }
    }

    /// <summary>True while the key is physically/logically down (high bit of GetAsyncKeyState).</summary>
    public static bool IsDown(ushort vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;
}
