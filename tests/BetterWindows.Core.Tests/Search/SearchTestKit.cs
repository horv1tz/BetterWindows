using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using Xunit;

namespace BetterWindows.Core.Tests.Search;

/// <summary>An app catalog the tests fill directly (the real AppCatalog is another stream's).</summary>
public sealed class FakeCatalog : IAppCatalog
{
    public List<AppEntry> Items { get; } = new();
    public IReadOnlyList<AppEntry> Apps => Items.ToArray();
    public AppEntry? FindById(string id) => Items.FirstOrDefault(a => a.Id == id);
    public AppEntry? FindByAumid(string aumid) => Items.FirstOrDefault(a => string.Equals(a.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
    public AppEntry? FindByExePath(string exePath) => Items.FirstOrDefault(a => string.Equals(a.Path, exePath, StringComparison.OrdinalIgnoreCase));
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event Action? Changed { add { } remove { } }

    public static AppEntry Aumid(string name, string aumid, string? path = null) => new()
    {
        Id = "aumid:" + aumid, Name = name, Aumid = aumid, Path = path, IconUrl = "/dyn/icon/app/x",
    };

    public static AppEntry Exe(string name, string path) => new()
    {
        Id = "exe:" + path.ToLowerInvariant(), Name = name, Path = path, IconUrl = "/dyn/icon/app/x",
    };
}

/// <summary>A provider whose behaviour the test scripts: a delegate that returns hits, waits, throws or ignores cancellation.</summary>
public sealed class ScriptedProvider : ISearchProvider
{
    public ScriptedProvider(SearchProviderId id, Func<SearchQuery, CancellationToken, Task<IReadOnlyList<SearchHit>>> run, TimeSpan? timeout = null)
    {
        Id = id;
        Run = run;
        Timeout = timeout ?? TimeSpan.FromMilliseconds(200);
    }

    public SearchProviderId Id { get; }
    public TimeSpan Timeout { get; }
    public Func<SearchQuery, CancellationToken, Task<IReadOnlyList<SearchHit>>> Run { get; }
    public int Calls { get; private set; }
    public bool WasCancelled { get; private set; }

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        Calls++;
        return Run(query, ct);
    }

    /// <summary>Returns the given hits at once.</summary>
    public static ScriptedProvider Immediate(SearchProviderId id, params SearchHit[] hits) =>
        new(id, (_, _) => Task.FromResult<IReadOnlyList<SearchHit>>(hits));

    /// <summary>Returns hits when the gate is completed.</summary>
    public static (ScriptedProvider Provider, TaskCompletionSource<IReadOnlyList<SearchHit>> Gate) Gated(SearchProviderId id, TimeSpan? timeout = null)
    {
        var gate = new TaskCompletionSource<IReadOnlyList<SearchHit>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider(id, (_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled(ct));
            return gate.Task;
        }, timeout);
        return (provider, gate);
    }
}

/// <summary>Builders and assertions shared by the search tests.</summary>
public static class SearchTestKit
{
    public static SearchHit Hit(SearchProviderId provider, string title, double score = 0.5, params HitAction[] actions) =>
        new(provider, title, null, null, score, actions.Length == 0 ? new[] { new HitAction("run", "Run", true, new CopyTextOperation(title)) } : actions);

    public static HitAction Primary(SearchOperation op, string id = "run") => new(id, "Run", true, op);

    /// <summary>Waits (real time, clock not advanced) until a scripted provider has been invoked. Test plumbing: a provider that has not started yet must not see the clock jump past its timeout.</summary>
    public static async Task WaitStartedAsync(ScriptedProvider provider, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (provider.Calls == 0)
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Xunit.Sdk.XunitException("provider was never invoked");
            await Task.Delay(1);
        }
    }

    /// <summary>Advances the manual clock in small steps until the task completes, so timers armed by background work fire (test plumbing only).</summary>
    public static async Task PumpAsync(ManualTimeProvider time, Task task, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Xunit.Sdk.XunitException("task did not complete while pumping the clock");
            time.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(2);
        }
        await task;
    }

    /// <summary>Waits (pumping the clock) until a condition holds.</summary>
    public static async Task PumpUntilAsync(ManualTimeProvider time, Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Xunit.Sdk.XunitException("condition not reached while pumping the clock");
            time.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(2);
        }
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bw-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
