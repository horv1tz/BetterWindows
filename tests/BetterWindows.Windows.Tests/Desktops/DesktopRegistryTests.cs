using BetterWindows.Native;

namespace BetterWindows.Native.Tests.Desktops;

public class DesktopRegistryTests
{
    // {AA509086-5CA9-4C25-8F95-589D3C07B48A} in the little-endian field layout used by REG_BINARY values.
    private static readonly byte[] VdmClsidBlob =
    {
        0x86, 0x90, 0x50, 0xAA, 0xA9, 0x5C, 0x25, 0x4C, 0x8F, 0x95, 0x58, 0x9D, 0x3C, 0x07, 0xB4, 0x8A,
    };

    [Fact]
    public void ParseGuidBlob_decodes_the_little_endian_field_layout()
    {
        var ids = DesktopRegistry.ParseGuidBlob(VdmClsidBlob);
        ids.Should().ContainSingle().Which.Should().Be(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
    }

    [Fact]
    public void ParseGuidBlob_keeps_order_and_drops_duplicates_and_empty()
    {
        var a = new Guid("11111111-2222-3333-4444-555555555555");
        var b = new Guid("66666666-7777-8888-9999-000000000000");
        var blob = a.ToByteArray().Concat(Guid.Empty.ToByteArray()).Concat(b.ToByteArray()).Concat(a.ToByteArray()).ToArray();
        DesktopRegistry.ParseGuidBlob(blob).Should().Equal(a, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(31)]
    public void ParseGuidBlob_rejects_misaligned_or_empty_blobs(int length)
    {
        DesktopRegistry.ParseGuidBlob(new byte[length]).Should().BeEmpty();
    }

    [Fact]
    public void ParseGuidBlob_null_is_empty()
    {
        DesktopRegistry.ParseGuidBlob(null).Should().BeEmpty();
    }

    [Fact]
    public void ParseCurrent_requires_exactly_one_guid()
    {
        DesktopRegistry.ParseCurrent(null).Should().BeNull();
        DesktopRegistry.ParseCurrent(new byte[32]).Should().BeNull();
        DesktopRegistry.ParseCurrent(new byte[16]).Should().BeNull(); // all zero = no desktop
        DesktopRegistry.ParseCurrent(VdmClsidBlob).Should().Be(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
    }

    [Theory]
    [InlineData("aa509086-5ca9-4c25-8f95-589d3c07b48a", true)]
    [InlineData("{AA509086-5CA9-4C25-8F95-589D3C07B48A}", true)]
    [InlineData("AA509086-5CA9-4C25-8F95-589D3C07B48A", true)]
    [InlineData("", false)]
    [InlineData(" aa509086-5ca9-4c25-8f95-589d3c07b48a", false)]
    [InlineData("aa5090865ca94c258f95589d3c07b48a", false)]          // "N" format is not accepted
    [InlineData("aa509086-5ca9-4c25-8f95-589d3c07b48g", false)]
    [InlineData("..\\..\\Windows", false)]
    [InlineData(null, false)]
    public void TryParseId_accepts_only_canonical_forms(string? text, bool expected)
    {
        DesktopRegistry.TryParseId(text, out var id).Should().Be(expected);
        if (expected) id.Should().Be(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
    }

    [Fact]
    public void FormatId_round_trips_through_TryParseId()
    {
        var id = Guid.NewGuid();
        var text = DesktopRegistry.FormatId(id);
        text.Should().Be(text.ToLowerInvariant());
        DesktopRegistry.TryParseId(text, out var back).Should().BeTrue();
        back.Should().Be(id);
    }

    [Theory]
    [InlineData(0, "Desktop 1")]
    [InlineData(1, "Desktop 2")]
    [InlineData(15, "Desktop 16")]
    public void DefaultName_uses_the_english_pattern(int index, string expected)
    {
        DesktopRegistry.DefaultName(index).Should().Be(expected);
    }

    [Fact]
    public void ResolveCurrentIndex_falls_back_to_the_first_desktop()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        DesktopRegistry.ResolveCurrentIndex(ids, null).Should().Be(0);
        DesktopRegistry.ResolveCurrentIndex(ids, Guid.NewGuid()).Should().Be(0);
        DesktopRegistry.ResolveCurrentIndex(ids, ids[2]).Should().Be(2);
        DesktopRegistry.ResolveCurrentIndex(Array.Empty<Guid>(), ids[0]).Should().Be(-1);
    }

    [Fact]
    public void BuildDesktops_names_blank_entries_with_the_default_pattern_and_marks_current()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var names = new Dictionary<Guid, string?> { [ids[0]] = "Work", [ids[1]] = "   " };
        var list = DesktopRegistry.BuildDesktops(ids, id => names.GetValueOrDefault(id), ids[1]);

        list.Select(d => d.Name).Should().Equal("Work", "Desktop 2", "Desktop 3");
        list.Select(d => d.Index).Should().Equal(0, 1, 2);
        list.Select(d => d.Current).Should().Equal(false, true, false);
        list[0].Id.Should().Be(DesktopRegistry.FormatId(ids[0]));
    }

    [Fact]
    public void BuildDesktops_truncates_hostile_long_names()
    {
        var id = Guid.NewGuid();
        var list = DesktopRegistry.BuildDesktops(new[] { id }, _ => new string('x', 5000), id);
        list.Single().Name.Length.Should().Be(DesktopRegistry.MaxNameLength);
    }

    [Fact]
    public void BuildDesktops_empty_list_for_no_desktops()
    {
        DesktopRegistry.BuildDesktops(Array.Empty<Guid>(), _ => null, null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 3, true, 1, 3)]
    [InlineData(0, 2, true, 1, 2)]
    [InlineData(2, 0, true, -1, 2)]
    [InlineData(1, 1, true, 0, 0)]
    [InlineData(0, 0, true, 0, 0)]
    public void Planner_computes_direction_and_steps(int current, int target, bool valid, int direction, int steps)
    {
        var count = Math.Max(current, target) + 1;
        var plan = DesktopSwitchPlanner.Plan(current, target, count);
        plan.Should().Be(new DesktopSwitchPlan(valid, direction, steps));
    }

    [Theory]
    [InlineData(-1, 0, 2)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, -1)]
    public void Planner_rejects_out_of_range_input(int current, int target, int count)
    {
        DesktopSwitchPlanner.Plan(current, target, count).Valid.Should().BeFalse();
    }

    [Fact]
    public void Planner_steps_equal_the_index_distance_for_every_pair()
    {
        for (int count = 1; count <= 16; count++)
        for (int a = 0; a < count; a++)
        for (int b = 0; b < count; b++)
        {
            var plan = DesktopSwitchPlanner.Plan(a, b, count);
            plan.Valid.Should().BeTrue();
            plan.Steps.Should().Be(Math.Abs(a - b));
            (a + plan.Direction * plan.Steps).Should().Be(b);
        }
    }
}
