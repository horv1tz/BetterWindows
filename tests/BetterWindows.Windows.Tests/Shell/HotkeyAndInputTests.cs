using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Bookkeeping of the hotkey registrar; runs everywhere.</summary>
public class HotkeyTableTests
{
    private static readonly HotkeyGesture CtrlAltQ = new(HotkeyGesture.Control | HotkeyGesture.Alt, 'Q');
    private static readonly HotkeyGesture AltSpace = new(HotkeyGesture.Alt, 0x20);

    [Fact]
    public void Ids_are_allocated_per_name_and_resolvable_from_WM_HOTKEY()
    {
        var t = new HotkeyTable();
        t.TryReserve("quit", CtrlAltQ, out int a, out _).Should().BeTrue();
        t.TryReserve("spotlight", AltSpace, out int b, out _).Should().BeTrue();

        a.Should().NotBe(b);
        t.TryGetName(a, out var name).Should().BeTrue();
        name.Should().Be("quit");
        t.TryGetName(b, out name).Should().BeTrue();
        name.Should().Be("spotlight");
        t.TryGetName(9999, out _).Should().BeFalse();
        t.Count.Should().Be(2);
    }

    [Fact]
    public void The_same_gesture_under_another_name_is_refused_and_names_the_owner()
    {
        var t = new HotkeyTable();
        t.TryReserve("a", CtrlAltQ, out _, out _);

        t.TryReserve("b", CtrlAltQ, out _, out string? conflict).Should().BeFalse();

        conflict.Should().Be("a");
        t.Count.Should().Be(1);
    }

    [Fact]
    public void Reserving_an_existing_name_is_refused_so_callers_must_release_first()
    {
        var t = new HotkeyTable();
        t.TryReserve("a", CtrlAltQ, out _, out _);
        t.TryReserve("a", AltSpace, out _, out string? conflict).Should().BeFalse();
        conflict.Should().Be("a");
    }

    [Fact]
    public void Release_frees_the_gesture_and_the_id()
    {
        var t = new HotkeyTable();
        t.TryReserve("a", CtrlAltQ, out int id, out _);

        t.Release("a", out int released).Should().BeTrue();
        released.Should().Be(id);
        t.Release("a", out _).Should().BeFalse();
        t.TryGetName(id, out _).Should().BeFalse();
        t.TryReserve("b", CtrlAltQ, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void Ids_stay_within_the_application_range_and_skip_used_ones_after_wrapping()
    {
        var t = new HotkeyTable();
        t.TryReserve("first", CtrlAltQ, out int first, out _);
        var seen = new HashSet<int> { first };
        for (uint vk = 1; vk <= 200; vk++)
        {
            t.TryReserve("k" + vk, new HotkeyGesture(HotkeyGesture.Win, vk + 1000), out int id, out _).Should().BeTrue();
            id.Should().BeInRange(1, HotkeyTable.MaxId);
            seen.Add(id).Should().BeTrue("ids must be unique");
        }
    }

    [Theory]
    [InlineData("", 'Q', 2u, false)]
    [InlineData("  ", 'Q', 2u, false)]
    [InlineData("a", 0u, 2u, false)]
    [InlineData("a", 255u, 2u, false)]
    [InlineData("a", 'Q', 0x10u, false)]  // unknown modifier bit
    [InlineData("a", 'Q', 2u, true)]
    [InlineData("a", 0x7Au, 0u, true)]    // F11 without modifier
    public void Validation(string id, uint vk, uint mods, bool valid) =>
        HotkeyTable.IsValid(id, new HotkeyGesture(mods, vk), out _).Should().Be(valid);

    [Fact]
    public void Id_length_is_capped() => HotkeyTable.IsValid(new string('x', 129), CtrlAltQ, out _).Should().BeFalse();

    [Fact]
    public void Clear_forgets_everything()
    {
        var t = new HotkeyTable();
        t.TryReserve("a", CtrlAltQ, out _, out _);
        t.Clear();
        t.Count.Should().Be(0);
        t.All().Should().BeEmpty();
    }
}

/// <summary>Key sequences of the input simulator; runs everywhere.</summary>
public class InputSequencesTests
{
    private static readonly IReadOnlySet<ushort> None = new HashSet<ushort>();

    private static string Render(IReadOnlyList<KeyStep> steps) =>
        string.Join(" ", steps.Select(s => $"{(s.KeyUp ? '-' : '+')}{s.VirtualKey:X2}{(s.Extended ? "e" : "")}"));

    [Fact]
    public void ShowDesktop_is_LWin_down_D_down_D_up_LWin_up_with_the_win_key_flagged_extended() =>
        Render(InputSequences.ShowDesktop(None)).Should().Be("+5Be +44 -44 -5Be");

    [Fact]
    public void Desktop_switch_uses_ctrl_and_extended_arrow_keys_with_reverse_release()
    {
        Render(InputSequences.SwitchDesktop(None, left: true)).Should().Be("+5Be +A2 +25e -25e -A2 -5Be");
        Render(InputSequences.SwitchDesktop(None, left: false)).Should().Be("+5Be +A2 +27e -27e -A2 -5Be");
    }

    [Fact]
    public void TaskView_is_Win_Tab_and_tab_is_not_extended() =>
        Render(InputSequences.TaskView(None)).Should().Be("+5Be +09 -09 -5Be");

    [Fact]
    public void Foreign_held_modifiers_are_released_first_so_Alt_Space_does_not_become_Win_Alt_D()
    {
        var held = new HashSet<ushort> { Win32Consts.VK_LMENU, Win32Consts.VK_RCONTROL };

        var steps = InputSequences.ShowDesktop(held);

        Render(steps).Should().Be("-A3e -A4 +5Be +44 -44 -5Be");
    }

    [Fact]
    public void A_held_modifier_that_is_part_of_the_chord_is_not_released_early()
    {
        var held = new HashSet<ushort> { Win32Consts.VK_LCONTROL, Win32Consts.VK_RWIN };

        var steps = InputSequences.SwitchDesktop(held, left: true);

        // RWin is foreign (we press LWin) and is released first; LCtrl is part of the chord so it is simply pressed again.
        Render(steps).Should().Be("-5Ce +5Be +A2 +25e -25e -A2 -5Be");
    }

    [Fact]
    public void Every_chord_is_balanced_every_key_pressed_is_released()
    {
        foreach (var steps in new[]
                 {
                     InputSequences.ShowDesktop(None),
                     InputSequences.SwitchDesktop(None, true),
                     InputSequences.SwitchDesktop(new HashSet<ushort> { Win32Consts.VK_LSHIFT }, false),
                     InputSequences.TaskView(None),
                 })
        {
            var down = steps.Where(s => !s.KeyUp).Select(s => s.VirtualKey).OrderBy(x => x).ToArray();
            var up = steps.Where(s => s.KeyUp).Select(s => s.VirtualKey).Distinct().ToArray();
            foreach (ushort k in down.Distinct())
            {
                up.Should().Contain(k);
                steps.Last(x => x.VirtualKey == k).KeyUp.Should().BeTrue("the final event for every key is its release");
            }
        }
    }

    [Fact]
    public void ReleaseAll_emits_one_key_up_per_key_in_reverse_order()
    {
        var steps = InputSequences.SwitchDesktop(None, left: true);
        Render(InputSequences.ReleaseAll(steps)).Should().Be("-25e -A2 -5Be");
    }

    [Fact]
    public void HeldModifiers_uses_the_probe_for_all_eight_modifier_keys()
    {
        var probed = new List<ushort>();
        var held = InputSequences.HeldModifiers(vk =>
        {
            probed.Add(vk);
            return vk == Win32Consts.VK_RSHIFT;
        });

        held.Should().BeEquivalentTo(new[] { Win32Consts.VK_RSHIFT });
        probed.Should().BeEquivalentTo(InputSequences.ModifierKeys);
        InputSequences.ModifierKeys.Should().HaveCount(8);
    }

    [Theory]
    [InlineData(0x25, true)]
    [InlineData(0x26, true)]
    [InlineData(0x27, true)]
    [InlineData(0x28, true)]
    [InlineData(0x5B, true)]
    [InlineData(0x5C, true)]
    [InlineData(0xA3, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x2E, true)]
    [InlineData(0x44, false)]
    [InlineData(0x09, false)]
    [InlineData(0xA2, false)]
    [InlineData(0xA4, false)]
    public void Extended_key_flag(int vk, bool extended) => KeyboardInput.IsExtended((ushort)vk).Should().Be(extended);
}
