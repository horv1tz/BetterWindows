using BetterWindows.Core.Input;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Input;

public class HotkeyParserTests
{
    private const uint Alt = HotkeyGesture.Alt, Ctrl = HotkeyGesture.Control, Shift = HotkeyGesture.Shift, Win = HotkeyGesture.Win;

    [Theory]
    [InlineData("Ctrl+Alt+Q", Ctrl | Alt, 0x51u)]
    [InlineData("Alt+Space", Alt, 0x20u)]
    [InlineData("Win+Shift+S", Win | Shift, 0x53u)]
    [InlineData("F4", 0u, 0x73u)]
    [InlineData("Ctrl+Alt+Shift+T", Ctrl | Alt | Shift, 0x54u)]
    [InlineData("Ctrl+Alt+L", Ctrl | Alt, 0x4Cu)]
    [InlineData("Ctrl+Alt+M", Ctrl | Alt, 0x4Du)]
    [InlineData("Ctrl+Alt+Shift+Win+Delete", Ctrl | Alt | Shift | Win, 0x2Eu)]
    public void Parses_documented_examples(string text, uint mods, uint vk)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.Should().Be(new HotkeyGesture(mods, vk));
    }

    [Theory]
    [InlineData("ctrl+alt+q")]
    [InlineData("CTRL+ALT+Q")]
    [InlineData("CtRl+aLt+q")]
    [InlineData("Control+Alt+Q")]
    [InlineData("control+alt+q")]
    [InlineData("Ctrl + Alt + Q")]
    [InlineData("  Ctrl+Alt+Q  ")]
    [InlineData("Alt+Ctrl+Q")]
    [InlineData("Q+Ctrl+Alt")]
    [InlineData("Ctrl +Alt+ Q")]
    [InlineData("\tCtrl+\tAlt+Q\n")]
    public void Is_case_insensitive_order_independent_and_tolerant_of_spaces(string text)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.Should().Be(new HotkeyGesture(Ctrl | Alt, 0x51));
    }

    [Theory]
    [InlineData("Win+Space")]
    [InlineData("Super+Space")]
    [InlineData("super+space")]
    public void Win_is_spelled_Win_or_Super(string text)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.Should().Be(new HotkeyGesture(Win, 0x20));
    }

    [Theory]
    [InlineData("Ctrl+A", 0x41u)]
    [InlineData("Ctrl+M", 0x4Du)]
    [InlineData("Ctrl+Z", 0x5Au)]
    [InlineData("Ctrl+0", 0x30u)]
    [InlineData("Ctrl+1", 0x31u)]
    [InlineData("Ctrl+9", 0x39u)]
    public void Letters_and_digits_map_to_their_ascii_virtual_keys(string text, uint vk)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.VirtualKey.Should().Be(vk);
    }

    [Theory]
    [InlineData("F1", 0x70u)]
    [InlineData("F2", 0x71u)]
    [InlineData("F10", 0x79u)]
    [InlineData("F12", 0x7Bu)]
    [InlineData("F13", 0x7Cu)]
    [InlineData("F24", 0x87u)]
    [InlineData("f5", 0x74u)]
    public void Function_keys_need_no_modifier(string text, uint vk)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.Modifiers.Should().Be(0u);
        g.VirtualKey.Should().Be(vk);
    }

    [Theory]
    [InlineData("Shift+F10", Shift, 0x79u)]
    [InlineData("Ctrl+F24", Ctrl, 0x87u)]
    [InlineData("Alt+F4", Alt, 0x73u)]
    public void Function_keys_accept_modifiers_too(string text, uint mods, uint vk)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeTrue();
        g.Should().Be(new HotkeyGesture(mods, vk));
    }

    [Theory]
    [InlineData("Space", 0x20u)]
    [InlineData("Enter", 0x0Du)]
    [InlineData("Tab", 0x09u)]
    [InlineData("Esc", 0x1Bu)]
    [InlineData("Backspace", 0x08u)]
    [InlineData("Delete", 0x2Eu)]
    [InlineData("Insert", 0x2Du)]
    [InlineData("Home", 0x24u)]
    [InlineData("End", 0x23u)]
    [InlineData("PageUp", 0x21u)]
    [InlineData("PageDown", 0x22u)]
    [InlineData("Up", 0x26u)]
    [InlineData("Down", 0x28u)]
    [InlineData("Left", 0x25u)]
    [InlineData("Right", 0x27u)]
    [InlineData("Comma", 0xBCu)]
    [InlineData("Period", 0xBEu)]
    [InlineData("Slash", 0xBFu)]
    [InlineData("Backslash", 0xDCu)]
    [InlineData("Semicolon", 0xBAu)]
    [InlineData("Quote", 0xDEu)]
    [InlineData("Minus", 0xBDu)]
    [InlineData("Equals", 0xBBu)]
    [InlineData("Backtick", 0xC0u)]
    [InlineData("LeftBracket", 0xDBu)]
    [InlineData("RightBracket", 0xDDu)]
    public void Named_keys_map_to_the_win32_virtual_key_codes(string key, uint vk)
    {
        HotkeyParser.TryParse("Ctrl+" + key, out var g).Should().BeTrue();
        g.Should().Be(new HotkeyGesture(Ctrl, vk));
    }

    [Theory]
    [InlineData("Num0", 0x60u)]
    [InlineData("Num1", 0x61u)]
    [InlineData("Num5", 0x65u)]
    [InlineData("Num9", 0x69u)]
    [InlineData("num7", 0x67u)]
    public void Numpad_keys_map_to_VK_NUMPAD(string key, uint vk)
    {
        HotkeyParser.TryParse("Alt+" + key, out var g).Should().BeTrue();
        g.VirtualKey.Should().Be(vk);
    }

    [Theory]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Return")]
    [InlineData("Ctrl+Del")]
    [InlineData("Ctrl+Ins")]
    [InlineData("Ctrl+PgUp")]
    [InlineData("Ctrl+PgDn")]
    [InlineData("Ctrl+Spacebar")]
    [InlineData("Ctrl+Grave")]
    [InlineData("Ctrl+Page Up")]
    [InlineData("Ctrl+Left Bracket")]
    [InlineData("Ctrl+,")]
    [InlineData("Ctrl+.")]
    [InlineData("Ctrl+/")]
    [InlineData("Ctrl+\\")]
    [InlineData("Ctrl+;")]
    [InlineData("Ctrl+'")]
    [InlineData("Ctrl+-")]
    [InlineData("Ctrl+=")]
    [InlineData("Ctrl+`")]
    [InlineData("Ctrl+[")]
    [InlineData("Ctrl+]")]
    [InlineData("Cmd+Q")]
    [InlineData("Command+Q")]
    [InlineData("Windows+Q")]
    public void Spellings_outside_the_documented_grammar_are_rejected(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Empty_input_is_rejected(string? text)
    {
        HotkeyParser.TryParse(text, out var g).Should().BeFalse();
        g.Should().Be(default(HotkeyGesture));
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+Shift+Win")]
    [InlineData("Win")]
    [InlineData("Shift+")]
    [InlineData("Ctrl+Alt+")]
    public void Modifier_only_is_rejected(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("Ctrl+Ctrl+Q")]
    [InlineData("Ctrl+Control+Q")]
    [InlineData("Win+Super+E")]
    [InlineData("Win+Win+E")]
    [InlineData("Alt+Alt+X")]
    [InlineData("shift+SHIFT+x")]
    public void Duplicate_modifiers_are_rejected_even_through_aliases(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F1+F2")]
    [InlineData("Alt+Space+Enter")]
    [InlineData("Q+W")]
    public void More_than_one_key_is_rejected(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("F25")]
    [InlineData("Ctrl+Num10")]
    [InlineData("Ctrl+Num")]
    [InlineData("Ctrl+AA")]
    [InlineData("Ctrl+10")]
    [InlineData("Ctrl+Plus")]
    [InlineData("Ctrl+Caps")]
    [InlineData("Ctrl+CapsLock")]
    [InlineData("Ctrl+Alt+Ç")]
    [InlineData("Ctrl+Alt+б")]
    [InlineData("Ctrl+Alt+\u0000")]
    [InlineData("Ctrl+Alt+Q\u0007")]
    [InlineData("Meta+Q")]
    [InlineData("Option+Q")]
    public void Unknown_keys_are_rejected(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("Q")]
    [InlineData("A")]
    [InlineData("5")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Esc")]
    [InlineData("Delete")]
    [InlineData("Left")]
    [InlineData("Num1")]
    [InlineData("Comma")]
    public void A_modifier_is_required_unless_the_key_is_a_function_key(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("+Q")]
    [InlineData("Ctrl++Q")]
    [InlineData("Ctrl+Alt+Q+")]
    [InlineData("+")]
    [InlineData("++")]
    [InlineData("Ctrl+ +Q")]
    [InlineData("Ctrl Alt Q")]
    [InlineData("CtrlAltQ")]
    [InlineData("Ctrl-Alt-Q")]
    [InlineData("Ctrl,Alt,Q")]
    public void Malformed_separators_are_rejected(string text) => HotkeyParser.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void Input_longer_than_64_characters_is_rejected()
    {
        HotkeyParser.TryParse(new string(' ', 70) + "Ctrl+Q", out _).Should().BeFalse();
        HotkeyParser.TryParse("Ctrl+" + new string('A', 70), out _).Should().BeFalse();
        var exactly64 = "Ctrl+Alt+Shift+Win+" + new string(' ', 64 - "Ctrl+Alt+Shift+Win+".Length - 1) + "Q";
        exactly64.Length.Should().Be(64);
        HotkeyParser.TryParse(exactly64, out _).Should().BeTrue();
        HotkeyParser.TryParse(exactly64 + " ", out _).Should().BeFalse();
    }

    [Fact]
    public void Nullable_overload_returns_null_for_invalid_and_the_gesture_for_valid_input()
    {
        HotkeyParser.TryParse("nope").Should().BeNull();
        HotkeyParser.TryParse(null).Should().BeNull();
        HotkeyParser.TryParse("Alt+Space").Should().Be(new HotkeyGesture(Alt, 0x20));
    }

    [Fact]
    public void Parse_throws_FormatException_on_invalid_input()
    {
        Action act = () => HotkeyParser.Parse("Ctrl");
        act.Should().Throw<FormatException>();
        HotkeyParser.Parse("Ctrl+Q").Should().Be(new HotkeyGesture(Ctrl, 0x51));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Q", true)]
    [InlineData("F4", true)]
    [InlineData("Q", false)]
    [InlineData("", false)]
    public void IsValid_matches_TryParse(string text, bool valid) => HotkeyParser.IsValid(text).Should().Be(valid);

    // ---- Format ----

    [Theory]
    [InlineData(Ctrl | Alt, 0x51u, "Ctrl+Alt+Q")]
    [InlineData(Alt, 0x20u, "Alt+Space")]
    [InlineData(Win | Shift, 0x53u, "Shift+Win+S")]
    [InlineData(0u, 0x73u, "F4")]
    [InlineData(Ctrl | Alt | Shift | Win, 0x2Eu, "Ctrl+Alt+Shift+Win+Delete")]
    [InlineData(Win, 0x44u, "Win+D")]
    [InlineData(Ctrl, 0x60u, "Ctrl+Num0")]
    [InlineData(Shift | Ctrl, 0xC0u, "Ctrl+Shift+Backtick")]
    public void Format_produces_the_canonical_string(uint mods, uint vk, string expected) =>
        HotkeyParser.Format(new HotkeyGesture(mods, vk)).Should().Be(expected);

    [Theory]
    [InlineData("win+shift+ctrl+alt+x", "Ctrl+Alt+Shift+Win+X")]
    [InlineData("  control + q  ", "Ctrl+Q")]
    [InlineData("super+space", "Win+Space")]
    [InlineData("alt+esc", "Alt+Esc")]
    [InlineData("f4", "F4")]
    [InlineData("ctrl+pageup", "Ctrl+PageUp")]
    public void TryNormalize_returns_the_canonical_spelling(string input, string expected)
    {
        HotkeyParser.TryNormalize(input, out var canonical).Should().BeTrue();
        canonical.Should().Be(expected);
    }

    [Fact]
    public void TryNormalize_fails_for_invalid_input()
    {
        HotkeyParser.TryNormalize("Ctrl", out var canonical).Should().BeFalse();
        canonical.Should().BeEmpty();
    }

    [Fact]
    public void Format_returns_empty_for_gestures_the_grammar_cannot_express()
    {
        HotkeyParser.Format(new HotkeyGesture(Ctrl, 0xFF)).Should().BeEmpty();          // unknown key
        HotkeyParser.Format(new HotkeyGesture(Ctrl, 0)).Should().BeEmpty();
        HotkeyParser.Format(new HotkeyGesture(0, 0x51)).Should().BeEmpty();             // no modifier for a non-F key
        HotkeyParser.Format(new HotkeyGesture(0x10, 0x51)).Should().BeEmpty();          // stray modifier bit
        HotkeyParser.Format(default).Should().BeEmpty();
        HotkeyParser.TryFormat(new HotkeyGesture(Ctrl, 0xFF), out var text).Should().BeFalse();
        text.Should().BeEmpty();
    }

    [Fact]
    public void Format_ignores_MOD_NOREPEAT()
    {
        HotkeyParser.Format(new HotkeyGesture(0x4000 | Ctrl | Alt, 0x51)).Should().Be("Ctrl+Alt+Q");
    }

    [Fact]
    public void Every_letter_digit_function_and_numpad_key_round_trips()
    {
        var keys = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        for (var c = '0'; c <= '9'; c++) keys.Add(c.ToString());
        for (var i = 1; i <= 24; i++) keys.Add("F" + i);
        for (var i = 0; i <= 9; i++) keys.Add("Num" + i);
        keys.Should().HaveCount(26 + 10 + 24 + 10);
        foreach (var key in keys)
        {
            var text = "Ctrl+Shift+" + key;
            HotkeyParser.TryParse(text, out var g).Should().BeTrue(text);
            HotkeyParser.Format(g).Should().Be(text);
        }
    }

    [Fact]
    public void Every_named_key_round_trips()
    {
        var names = new[] { "Space", "Enter", "Tab", "Esc", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
            "Backtick", "Minus", "Equals", "LeftBracket", "RightBracket", "Backslash", "Semicolon", "Quote", "Comma", "Period", "Slash" };
        foreach (var name in names)
        {
            var text = "Alt+" + name;
            HotkeyParser.TryParse(text, out var g).Should().BeTrue(text);
            HotkeyParser.Format(g).Should().Be(text);
        }
    }

    [Fact]
    public void Every_key_has_a_unique_virtual_key_code()
    {
        var seen = new Dictionary<uint, string>();
        var all = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++) all.Add(c.ToString());
        for (var c = '0'; c <= '9'; c++) all.Add(c.ToString());
        for (var i = 1; i <= 24; i++) all.Add("F" + i);
        for (var i = 0; i <= 9; i++) all.Add("Num" + i);
        all.AddRange(new[] { "Space", "Enter", "Tab", "Esc", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
            "Backtick", "Minus", "Equals", "LeftBracket", "RightBracket", "Backslash", "Semicolon", "Quote", "Comma", "Period", "Slash" });
        foreach (var k in all)
        {
            HotkeyParser.TryParse("Ctrl+" + k, out var g).Should().BeTrue(k);
            seen.TryAdd(g.VirtualKey, k).Should().BeTrue($"{k} collides with {(seen.TryGetValue(g.VirtualKey, out var other) ? other : "?")}");
        }
        seen.Should().HaveCount(all.Count);
    }

    [Fact]
    public void Property_every_formattable_gesture_parses_back_to_itself()
    {
        var count = 0;
        for (uint mods = 0; mods <= 0xF; mods++)
        {
            for (uint vk = 0; vk <= 0xFF; vk++)
            {
                var g = new HotkeyGesture(mods, vk);
                if (!HotkeyParser.TryFormat(g, out var text)) continue;
                count++;
                HotkeyParser.TryParse(text, out var back).Should().BeTrue(text);
                back.Should().Be(g, text);
            }
        }
        count.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void The_default_hotkeys_of_the_settings_schema_are_valid_and_distinct()
    {
        var s = new Settings();
        var defaults = new[] { s.Spotlight.Hotkey, s.Hotkeys.Launchpad, s.Hotkeys.MissionControl, s.Hotkeys.Exit, s.Hotkeys.ToggleTaskbar };
        var gestures = defaults.Select(d => { HotkeyParser.TryParse(d, out var g).Should().BeTrue(d); return g; }).ToArray();
        gestures.Distinct().Should().HaveCount(defaults.Length);
        defaults.Select(d => { HotkeyParser.TryNormalize(d, out var c); return c; }).Should().Equal(defaults, "the defaults are already canonical");
    }
}
