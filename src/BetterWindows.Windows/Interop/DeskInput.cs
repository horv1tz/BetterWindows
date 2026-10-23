using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Keyboard chords for the shell's virtual desktop shortcuts (Win+Ctrl+Left/Right, Win+Ctrl+D, Win+Ctrl+F4). These are documented Windows shortcuts,
/// so no internal API is needed. SendInput is used because the shortcuts are not registrable as hotkeys by an app.
/// </summary>
internal static class DeskInput
{
    internal const ushort VkLeft = 0x25;
    internal const ushort VkRight = 0x27;
    internal const ushort VkF4 = 0x73;
    internal const ushort VkD = 0x44;
    internal const ushort VkLWin = 0x5B;
    internal const ushort VkLControl = 0xA2;

    /// <summary>Presses the keys in order and releases them in reverse order. Returns false when SendInput did not inject every event (UIPI, blocked input).</summary>
    public static bool SendChord(params ushort[] keys)
    {
        if (keys.Length == 0) return false;
        // Extended-key flag: the arrow keys are "extended" keys (keyboard scan codes E0 xx); sending them without it can map to the numpad arrows.
        var inputs = new INPUT[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = Build(keys[i], keyUp: false);
            inputs[keys.Length + i] = Build(keys[keys.Length - 1 - i], keyUp: true);
        }
        uint sent = PInvoke.SendInput(inputs.AsSpan(), Marshal.SizeOf<INPUT>());
        return sent == (uint)inputs.Length;
    }

    private static INPUT Build(ushort vk, bool keyUp)
    {
        KEYBD_EVENT_FLAGS flags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (vk is VkLeft or VkRight) flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)vk, dwFlags = flags },
            },
        };
    }
}
