using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Lifecycle;

namespace BetterWindows.Host.Tests.Support;

/// <summary>A throwaway directory under the system temp path, deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, string content = "")
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Process probe whose "alive" set the test controls.</summary>
public sealed class FakeProbe : IProcessProbe
{
    public HashSet<int> Alive { get; } = new();
    public bool IsAlive(int pid, DateTimeOffset? expectedStartUtc) => Alive.Contains(pid);
}

/// <summary>Records what the channel would have posted to a page.</summary>
public sealed class FakeChannel : ISurfaceChannel
{
    public FakeChannel(SurfaceId id) { Id = id; }
    public SurfaceId Id { get; }
    public List<string> Posted { get; } = new();
    public void Post(string json) => Posted.Add(json);
}

/// <summary>UI scheduler that queues work until the test runs it.</summary>
public sealed class ManualUiScheduler : IUiScheduler
{
    private readonly Queue<Action> _queue = new();
    public bool OnUiThread { get; set; }
    public bool CheckAccess() => OnUiThread;
    public void Post(Action action) { lock (_queue) _queue.Enqueue(action); }
    public int Pending { get { lock (_queue) return _queue.Count; } }

    /// <summary>Runs everything queued so far (and whatever they queue) on the calling thread.</summary>
    public void RunAll()
    {
        while (true)
        {
            Action? next;
            lock (_queue) next = _queue.Count > 0 ? _queue.Dequeue() : null;
            if (next is null) return;
            next();
        }
    }
}

public static class Json
{
    /// <summary>A minimal valid event envelope for the given type (payload empty).</summary>
    public static string Event(string type) => $"{{\"v\":{ProtocolInfo.Version},\"kind\":\"event\",\"type\":\"{type}\",\"payload\":{{}}}}";
    public static string Result(string id) => $"{{\"v\":{ProtocolInfo.Version},\"kind\":\"result\",\"id\":\"{id}\",\"ok\":true,\"payload\":null}}";
}
