using BetterWindows.Core.Dock;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Dock;

public class PinsEditorTests
{
    private static PinnedItem A(string id) => new() { Id = id, Kind = PinnedKind.App, Target = id };
    private static PinnedItem F(string id) => new() { Id = id, Kind = PinnedKind.Folder, Target = id };
    private static string[] Ids(IEnumerable<PinnedItem> pins) => pins.Select(p => p.Id).ToArray();

    [Fact]
    public void Normalize_puts_apps_before_folders_keeps_relative_order_and_drops_duplicates_and_empties()
    {
        var result = PinsEditor.Normalize(new[] { F("f1"), A("a1"), A("A1"), A(""), F("f2"), A("a2") });
        Ids(result).Should().Equal("a1", "a2", "f1", "f2");
    }

    [Fact]
    public void Insert_defaults_to_the_end_of_the_apps_section()
    {
        Ids(PinsEditor.Insert(new[] { A("a1"), F("f1") }, A("a2"), null)).Should().Equal("a1", "a2", "f1");
    }

    [Theory]
    [InlineData(0, new[] { "new", "a1", "a2", "f1" })]
    [InlineData(1, new[] { "a1", "new", "a2", "f1" })]
    [InlineData(2, new[] { "a1", "a2", "new", "f1" })]
    [InlineData(99, new[] { "a1", "a2", "new", "f1" })]
    [InlineData(-5, new[] { "new", "a1", "a2", "f1" })]
    public void Insert_clamps_the_index_into_the_apps_section(int index, string[] expected)
    {
        Ids(PinsEditor.Insert(new[] { A("a1"), A("a2"), F("f1") }, A("new"), index)).Should().Equal(expected);
    }

    [Theory]
    [InlineData(0, new[] { "a1", "new", "f1" })]
    [InlineData(99, new[] { "a1", "f1", "new" })]
    public void A_folder_is_inserted_into_the_folders_section(int index, string[] expected)
    {
        Ids(PinsEditor.Insert(new[] { A("a1"), F("f1") }, F("new"), index)).Should().Equal(expected);
    }

    [Fact]
    public void Inserting_an_already_pinned_id_changes_nothing()
    {
        Ids(PinsEditor.Insert(new[] { A("a1"), A("a2") }, A("A2"), 0)).Should().Equal("a1", "a2");
    }

    [Fact]
    public void Inserting_beyond_the_limit_changes_nothing()
    {
        var full = Enumerable.Range(0, PinsEditor.MaxPins).Select(i => A("a" + i)).ToList();
        PinsEditor.Insert(full, A("extra"), null).Should().HaveCount(PinsEditor.MaxPins);
    }

    [Fact]
    public void Remove_is_case_insensitive_and_ignores_unknown_ids()
    {
        Ids(PinsEditor.Remove(new[] { A("a1"), A("a2") }, "A1")).Should().Equal("a2");
        Ids(PinsEditor.Remove(new[] { A("a1") }, "zzz")).Should().Equal("a1");
    }

    [Fact]
    public void Reorder_applies_the_given_order()
    {
        PinsEditor.TryReorder(new[] { A("a1"), A("a2"), A("a3") }, new[] { "a3", "a1", "a2" }, out var result, out var unknown).Should().BeTrue();
        unknown.Should().BeNull();
        Ids(result).Should().Equal("a3", "a1", "a2");
    }

    [Fact]
    public void Reorder_never_deletes_pins_that_the_caller_forgot_to_mention()
    {
        PinsEditor.TryReorder(new[] { A("a1"), A("a2"), A("a3") }, new[] { "a3" }, out var result, out _).Should().BeTrue();
        Ids(result).Should().Equal("a3", "a1", "a2");
    }

    [Fact]
    public void Reorder_ignores_duplicates_and_keeps_folders_last()
    {
        PinsEditor.TryReorder(new[] { A("a1"), A("a2"), F("f1") }, new[] { "f1", "a2", "A2", "a1" }, out var result, out _).Should().BeTrue();
        Ids(result).Should().Equal("a2", "a1", "f1");
    }

    [Fact]
    public void Reorder_reports_the_first_unknown_id_and_changes_nothing()
    {
        PinsEditor.TryReorder(new[] { A("a1") }, new[] { "a1", "ghost" }, out var result, out var unknown).Should().BeFalse();
        unknown.Should().Be("ghost");
        Ids(result).Should().Equal("a1");
    }
}
