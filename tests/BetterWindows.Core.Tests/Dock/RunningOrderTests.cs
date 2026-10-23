using BetterWindows.Core.Dock;

namespace BetterWindows.Core.Tests.Dock;

public class RunningOrderTests
{
    [Fact]
    public void New_apps_are_appended_in_the_given_order_and_known_apps_keep_their_place()
    {
        var o = new RunningOrder();
        o.Update(new[] { "a", "b" }).Should().Equal("a", "b");
        o.Update(new[] { "b", "c", "a" }).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void An_app_that_stops_running_is_forgotten_and_returns_at_the_end()
    {
        var o = new RunningOrder();
        o.Update(new[] { "a", "b", "c" });
        o.Update(new[] { "a", "c" }).Should().Equal("a", "c");
        o.Update(new[] { "a", "b", "c" }).Should().Equal("a", "c", "b");
    }

    [Fact]
    public void Ids_are_case_insensitive_and_duplicates_collapse()
    {
        var o = new RunningOrder();
        o.Update(new[] { "A", "a", "B" }).Should().Equal("A", "B");
        o.Update(new[] { "b", "a" }).Should().Equal("A", "B");
    }

    [Fact]
    public void An_unchanged_update_returns_the_same_list_instance()
    {
        var o = new RunningOrder();
        var first = o.Update(new[] { "a" });
        o.Update(new[] { "a" }).Should().BeSameAs(first);
    }
}
