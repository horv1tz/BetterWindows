using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Pure tables of the window hook layer; run on every OS.</summary>
public class WindowEventMapperTests
{
    [Theory]
    [InlineData(0x0003u, WindowEventKind.Foreground)]
    [InlineData(0x0016u, WindowEventKind.Minimized)]
    [InlineData(0x0017u, WindowEventKind.Restored)]
    [InlineData(0x8000u, WindowEventKind.Created)]
    [InlineData(0x8001u, WindowEventKind.Destroyed)]
    [InlineData(0x8002u, WindowEventKind.Shown)]
    [InlineData(0x8003u, WindowEventKind.Hidden)]
    [InlineData(0x800Bu, WindowEventKind.Moved)]
    [InlineData(0x800Cu, WindowEventKind.TitleChanged)]
    [InlineData(0x8017u, WindowEventKind.Cloaked)]
    [InlineData(0x8018u, WindowEventKind.Uncloaked)]
    public void WinEvents_map_to_window_event_kinds(uint winEvent, WindowEventKind expected)
    {
        WindowEventMapper.TryMap(winEvent, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData(0x0000u)]
    [InlineData(0x0004u)] // EVENT_SYSTEM_MENUSTART
    [InlineData(0x800Au)] // EVENT_OBJECT_REORDER... not interesting
    [InlineData(0x8004u)] // EVENT_OBJECT_FOCUS
    [InlineData(0x8019u)]
    public void Unknown_events_are_ignored(uint winEvent) => WindowEventMapper.TryMap(winEvent, out _).Should().BeFalse();

    [Fact]
    public void Hooks_are_narrow_and_every_id_they_deliver_is_mapped()
    {
        WindowEventMapper.HookRanges.Should().NotBeEmpty();
        foreach (var r in WindowEventMapper.HookRanges)
        {
            // Never EVENT_MIN..EVENT_MAX (0x1..0x7FFFFFFF): a hook may span at most a handful of consecutive ids.
            (r.Max - r.Min).Should().BeLessThan(8, r.Name);
            for (uint id = r.Min; id <= r.Max; id++)
            {
                WindowEventMapper.TryMap(id, out _).Should().BeTrue($"{r.Name} delivers 0x{id:X}");
            }
        }
    }

    [Fact]
    public void Hook_ranges_do_not_overlap_and_exclude_the_high_volume_location_event()
    {
        var all = WindowEventMapper.HookRanges.ToList();
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                (all[i].Max < all[j].Min || all[j].Max < all[i].Min).Should().BeTrue($"{all[i].Name} vs {all[j].Name}");
            }

            (all[i].Min <= Win32Consts.EVENT_OBJECT_LOCATIONCHANGE && Win32Consts.EVENT_OBJECT_LOCATIONCHANGE <= all[i].Max)
                .Should().BeFalse("LOCATIONCHANGE is installed lazily, never as part of the default hooks");
        }

        WindowEventMapper.LocationRange.Min.Should().Be(WindowEventMapper.LocationRange.Max);
    }

    [Fact]
    public void Only_bursty_kinds_are_throttled()
    {
        var throttled = Enum.GetValues<WindowEventKind>().Where(WindowEventMapper.IsThrottled).ToArray();
        throttled.Should().BeEquivalentTo(new[] { WindowEventKind.TitleChanged, WindowEventKind.Moved, WindowEventKind.Redraw });
    }

    [Theory]
    [InlineData(WindowEventKind.Foreground, true)]
    [InlineData(WindowEventKind.Moved, true)]
    [InlineData(WindowEventKind.Minimized, true)]
    [InlineData(WindowEventKind.Cloaked, true)]
    [InlineData(WindowEventKind.TitleChanged, false)]
    [InlineData(WindowEventKind.Created, false)]
    [InlineData(WindowEventKind.Flashed, false)]
    public void Fullscreen_relevant_kinds(WindowEventKind kind, bool expected) => WindowEventMapper.AffectsFullscreen(kind).Should().Be(expected);

    [Theory]
    [InlineData(0x8006, "Flash")]       // HSHELL_FLASH = HSHELL_REDRAW | HIGHBIT
    [InlineData(4, "Activated")]          // HSHELL_WINDOWACTIVATED
    [InlineData(0x8004, "Activated")]   // HSHELL_RUDEAPPACTIVATED
    [InlineData(6, "Redraw")]            // HSHELL_REDRAW
    [InlineData(1, "None")]              // HSHELL_WINDOWCREATED (covered by WinEvents)
    [InlineData(2, "None")]              // HSHELL_WINDOWDESTROYED
    [InlineData(0x35, "None")]
    public void Shell_hook_codes(int code, string expected) => WindowEventMapper.ClassifyShellHook(code).ToString().Should().Be(expected);
}

public class CloakInfoTests
{
    [Theory]
    [InlineData(0u, false, false)]
    [InlineData(1u, true, false)]   // DWM_CLOAKED_APP: suspended UWP - hidden by Core
    [InlineData(2u, true, true)]    // DWM_CLOAKED_SHELL: lives on another virtual desktop - real window
    [InlineData(4u, true, false)]   // DWM_CLOAKED_INHERITED: owned window of a cloaked owner
    [InlineData(3u, true, true)]
    public void Cloak_flags(uint flags, bool cloaked, bool byShell)
    {
        CloakInfo.IsCloaked(flags).Should().Be(cloaked);
        CloakInfo.IsCloakedByShell(flags).Should().Be(byShell);
    }
}
