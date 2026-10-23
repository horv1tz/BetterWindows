namespace BetterWindows.Native.Tests.Apps;

public class NaturalStringComparerTests
{
    private static string[] Sort(params string[] items) => items.OrderBy(i => i, NaturalStringComparer.Instance).ToArray();

    [Fact]
    public void Numbers_compare_by_value()
        => Sort("file10.txt", "file2.txt", "file1.txt", "file20.txt").Should().Equal("file1.txt", "file2.txt", "file10.txt", "file20.txt");

    [Fact]
    public void Case_is_ignored_for_ordering()
        => Sort("banana", "Apple", "cherry", "apple2", "Apple10").Should().Equal("Apple", "apple2", "Apple10", "banana", "cherry");

    [Fact]
    public void Leading_zeros_do_not_change_magnitude()
        => Sort("a007", "a10", "a06").Should().Equal("a06", "a007", "a10");

    [Fact]
    public void Prefix_sorts_first_and_ties_are_broken_deterministically()
    {
        NaturalStringComparer.Instance.Compare("abc", "abcd").Should().BeNegative();
        NaturalStringComparer.Instance.Compare("abcd", "abc").Should().BePositive();
        NaturalStringComparer.Instance.Compare("A", "a").Should().NotBe(0, "the order must be total so listings are stable");
        NaturalStringComparer.Instance.Compare("same", "same").Should().Be(0);
        NaturalStringComparer.Instance.Compare(null, "x").Should().BeNegative();
        NaturalStringComparer.Instance.Compare("x", null).Should().BePositive();
    }

    [Fact]
    public void Huge_digit_runs_do_not_overflow()
        => NaturalStringComparer.Instance.Compare("n" + new string('9', 60), "n1" + new string('0', 60)).Should().BeNegative();
}
