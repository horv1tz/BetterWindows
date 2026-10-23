using System.IO;
using System.Text.Json;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>Outcome of <see cref="StartupStateStore.BeginRun"/>.</summary>
/// <param name="PreviousExitUnclean">The previous run left a marker without a clean exit and its process is gone.</param>
/// <param name="RecentUncleanCount">Unclean, never-healthy starts within the last 10 minutes (not counting this one).</param>
/// <param name="CrashLoop">True when <paramref name="RecentUncleanCount"/> reached the threshold: start in SAFE MODE.</param>
public sealed record StartupDecision(bool PreviousExitUnclean, int RecentUncleanCount, bool CrashLoop);

/// <summary>
/// The crash counter of docs/IPC.md section 15 rule 5 (<c>state/startup.json</c>, also serves as the "running" marker: its pid says who is running).
/// A start is "unhealthy" until the process has run for 60 s. When the previous run neither exited cleanly nor became healthy and its pid is dead it is counted;
/// 3 such starts within 10 minutes mean a crash loop and the next start is SAFE MODE. A clean exit or 60 s of healthy running clear the counter.
/// All I/O goes through <see cref="AtomicFile"/>; failures are logged and never block startup.
/// </summary>
public sealed class StartupStateStore
{
    /// <summary>Unclean starts that trigger safe mode.</summary>
    public const int UncleanThreshold = 3;
    /// <summary>Window in which unclean starts are counted.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    /// <summary>A run is healthy after this long.</summary>
    public static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(60);
    private const int MaxFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, AllowTrailingCommas = true };

    private sealed class Record
    {
        public int Version { get; set; } = 1;
        public int Pid { get; set; }
        public DateTimeOffset StartUtc { get; set; }
        public bool Clean { get; set; }
        public bool Healthy { get; set; }
        public List<DateTimeOffset> RecentUnclean { get; set; } = new();
    }

    private readonly string _file;
    private readonly TimeProvider _time;
    private readonly IProcessProbe _probe;
    private readonly int _pid;
    private readonly ILogger? _log;
    private readonly object _gate = new();

    public StartupStateStore(string stateDir, TimeProvider time, IProcessProbe probe, int currentPid, ILogger? log = null)
    {
        _file = Path.Combine(stateDir, "startup.json");
        _time = time;
        _probe = probe;
        _pid = currentPid;
        _log = log;
    }

    /// <summary>Evaluates the previous run and records this one as running (unclean, unhealthy) until <see cref="MarkCleanExit"/>/<see cref="MarkHealthy"/>.</summary>
    public StartupDecision BeginRun()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var prev = Load();
            var recent = (prev?.RecentUnclean ?? new List<DateTimeOffset>()).Where(t => t <= now && now - t <= Window).ToList();
            var prevUnclean = false;
            if (prev is { Clean: false } && !_probe.IsAlive(prev.Pid, prev.StartUtc))
            {
                prevUnclean = true;
                // A run that reached "healthy" cleared the list; if it still died later it is an unclean exit but not part of a startup crash loop.
                if (!prev.Healthy && prev.StartUtc <= now && now - prev.StartUtc <= Window) recent.Add(prev.StartUtc);
            }
            var decision = new StartupDecision(prevUnclean, recent.Count, recent.Count >= UncleanThreshold);
            Save(new Record { Pid = _pid, StartUtc = now, Clean = false, Healthy = false, RecentUnclean = recent });
            if (prevUnclean) _log?.LogWarning("Previous run did not exit cleanly ({Count} unclean start(s) in the last {Minutes} min)", recent.Count, Window.TotalMinutes);
            return decision;
        }
    }

    /// <summary>The process ran for <see cref="HealthyAfter"/>: the crash counter resets.</summary>
    public void MarkHealthy() => Update(r => { r.Healthy = true; r.RecentUnclean.Clear(); });

    /// <summary>Orderly shutdown: the next start is a normal start.</summary>
    public void MarkCleanExit() => Update(r => { r.Clean = true; r.Healthy = true; r.RecentUnclean.Clear(); });

    /// <summary>Only the record this process wrote is modified (a newer instance's record is never clobbered).</summary>
    private void Update(Action<Record> change)
    {
        lock (_gate)
        {
            var r = Load();
            if (r is null || r.Pid != _pid) return;
            change(r);
            Save(r);
        }
    }

    private Record? Load()
    {
        try
        {
            var bytes = AtomicFile.TryRead(_file, MaxFileBytes, out _);
            return bytes is null ? null : JsonSerializer.Deserialize<Record>(bytes, Json);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "startup.json unreadable; treating as a first start");
            return null;
        }
    }

    private void Save(Record r)
    {
        try { AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(r, Json)); }
        catch (Exception ex) { _log?.LogWarning(ex, "Could not write startup.json"); }
    }
}
