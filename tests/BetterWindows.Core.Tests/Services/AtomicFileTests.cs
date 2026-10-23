using System.Text;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Services;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-af-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Write_creates_directories_and_replaces_atomically_leaving_no_temp_files()
    {
        var f = Path.Combine(_dir, "a", "config.json");
        AtomicFile.WriteAllText(f, "one");
        AtomicFile.WriteAllText(f, "two", keepBackup: true);
        File.ReadAllText(f).Should().Be("two");
        File.ReadAllText(f + ".bak").Should().Be("one");
        Directory.GetFiles(Path.GetDirectoryName(f)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void TryRead_respects_the_size_cap()
    {
        var f = Path.Combine(_dir, "big.json");
        AtomicFile.WriteAllBytes(f, new byte[1000]);
        AtomicFile.TryRead(f, 2000, out var tooLarge)!.Length.Should().Be(1000);
        tooLarge.Should().BeFalse();
        AtomicFile.TryRead(f, 999, out tooLarge).Should().BeNull();
        tooLarge.Should().BeTrue();
        AtomicFile.TryRead(Path.Combine(_dir, "missing"), 10, out tooLarge).Should().BeNull();
    }

    [Fact]
    public void Quarantine_moves_the_file_aside_and_keeps_only_the_newest_copies()
    {
        var time = new ManualTimeProvider();
        var f = Path.Combine(_dir, "config.json");
        for (var i = 0; i < 8; i++)
        {
            AtomicFile.WriteAllText(f, "broken" + i);
            AtomicFile.Quarantine(f, time, keep: 3).Should().NotBeNull();
            File.Exists(f).Should().BeFalse();
            time.Advance(TimeSpan.FromSeconds(1));
        }
        Directory.GetFiles(_dir, "config.corrupt-*.json").Should().HaveCount(3);
    }

    [Fact]
    public void Concurrent_writers_never_leave_a_torn_file()
    {
        var f = Path.Combine(_dir, "race.json");
        Parallel.For(0, 40, i => AtomicFile.WriteAllText(f, new string((char)('a' + i % 26), 5000)));
        var text = File.ReadAllText(f);
        text.Length.Should().Be(5000);
        text.Distinct().Should().HaveCount(1);
    }
}
