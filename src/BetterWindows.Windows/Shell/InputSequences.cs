using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>
/// Pure builders for the key sequences the input simulator injects. Ordering rules: modifiers go down before the key and come up in
/// reverse order after it; extended-key flags are set per key; before the sequence, any OTHER modifier the user is currently holding
/// (e.g. the Alt of an Alt+Space hotkey, a right-hand Ctrl) is released so it cannot turn "Win+D" into "Win+Alt+D". Cross-platform, unit-tested.
/// </summary>
internal static class InputSequences
{
    /// <summary>Every modifier virtual key (left and right variants) that can corrupt a synthesized chord.</summary>
    public static readonly IReadOnlyList<ushort> ModifierKeys = new ushort[]
    {
        Win32Consts.VK_LSHIFT, Win32Consts.VK_RSHIFT,
        Win32Consts.VK_LCONTROL, Win32Consts.VK_RCONTROL,
        Win32Consts.VK_LMENU, Win32Consts.VK_RMENU,
        Win32Consts.VK_LWIN, Win32Consts.VK_RWIN,
    };

    /// <summary>Win+D (show desktop).</summary>
    public static IReadOnlyList<KeyStep> ShowDesktop(IReadOnlySet<ushort> held) =>
        Chord(held, new[] { Win32Consts.VK_LWIN }, Win32Consts.VK_D);

    /// <summary>Win+Ctrl+Left / Win+Ctrl+Right (previous / next virtual desktop).</summary>
    public static IReadOnlyList<KeyStep> SwitchDesktop(IReadOnlySet<ushort> held, bool left) =>
        Chord(held, new[] { Win32Consts.VK_LWIN, Win32Consts.VK_LCONTROL }, left ? Win32Consts.VK_LEFT : Win32Consts.VK_RIGHT);

    /// <summary>Win+Tab (Task View).</summary>
    public static IReadOnlyList<KeyStep> TaskView(IReadOnlySet<ushort> held) =>
        Chord(held, new[] { Win32Consts.VK_LWIN }, Win32Consts.VK_TAB);

    /// <summary>The currently held modifiers, given a probe for "is this key down".</summary>
    public static IReadOnlySet<ushort> HeldModifiers(Func<ushort, bool> isDown)
    {
        var set = new HashSet<ushort>();
        foreach (ushort vk in ModifierKeys)
        {
            if (isDown(vk))
            {
                set.Add(vk);
            }
        }

        return set;
    }

    /// <summary>Key-up events for every key of <paramref name="sequence"/>, in reverse first-use order: the recovery when SendInput only partly succeeded.</summary>
    public static IReadOnlyList<KeyStep> ReleaseAll(IReadOnlyList<KeyStep> sequence)
    {
        var keys = new List<ushort>();
        foreach (var s in sequence)
        {
            if (!keys.Contains(s.VirtualKey))
            {
                keys.Add(s.VirtualKey);
            }
        }

        keys.Reverse();
        return keys.Select(KeyboardInput.Up).ToArray();
    }

    private static IReadOnlyList<KeyStep> Chord(IReadOnlySet<ushort> held, ushort[] modifiers, ushort key)
    {
        var steps = new List<KeyStep>(modifiers.Length * 2 + 2 + held.Count);

        // Cleanup: release held modifiers that are not part of the chord (our own modifiers are pressed fresh below).
        foreach (ushort m in ModifierKeys)
        {
            if (held.Contains(m) && Array.IndexOf(modifiers, m) < 0)
            {
                steps.Add(KeyboardInput.Up(m));
            }
        }

        foreach (ushort m in modifiers)
        {
            steps.Add(KeyboardInput.Down(m));
        }

        steps.Add(KeyboardInput.Down(key));
        steps.Add(KeyboardInput.Up(key));
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            steps.Add(KeyboardInput.Up(modifiers[i]));
        }

        return steps;
    }
}
