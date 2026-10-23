using System.Data.OleDb;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// File search over the Windows Search index (OLE DB provider <c>Search.CollatorDSO</c>, table <c>SYSTEMINDEX</c>) - the same source Explorer's search box and PowerToys Run use.
/// Results stream while the query runs (a worker thread feeds a channel; cancelling the token cancels the command and disposes reader and connection), name matches come first,
/// each phase ordered by System.Search.Rank. Availability = the WSearch service is running AND the provider answers a probe; the answer is cached briefly.
/// </summary>
public sealed class WindowsSearchFileSearch : IFileSearch
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\"";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PositiveCache = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCache = TimeSpan.FromSeconds(30);
    private const int CommandTimeoutSeconds = 5;

    private readonly ILogger _log;
    private readonly object _probeGate = new();
    private bool _available;
    private DateTime _availableUntilUtc = DateTime.MinValue;

    /// <summary>Creates the search provider.</summary>
    public WindowsSearchFileSearch(ILogger<WindowsSearchFileSearch>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            lock (_probeGate)
            {
                if (DateTime.UtcNow < _availableUntilUtc) return _available;
                _available = Probe();
                _availableUntilUtc = DateTime.UtcNow + (_available ? PositiveCache : NegativeCache);
                return _available;
            }
        }
    }

    private bool Probe()
    {
        try
        {
            // 1. The service must run: when it is stopped/disabled the OLE DB open can hang or fail slowly.
            bool? running = AppsServiceState.IsRunning("WSearch");
            if (running == false)
            {
                _log.LogInformation("Windows Search service (WSearch) is not running; file search disabled");
                return false;
            }
            // 2. The provider must answer a trivial query. Run it off-thread with a deadline so a wedged provider cannot hang the caller.
            var probe = Task.Run(() =>
            {
                using var conn = new OleDbConnection(ConnectionString);
                conn.Open();
                using var cmd = new OleDbCommand(FileSearchQuery.ProbeStatement, conn) { CommandTimeout = 3 };
                using var reader = cmd.ExecuteReader();
                return true;
            });
            if (!probe.Wait(ProbeTimeout))
            {
                _log.LogWarning("Windows Search provider probe timed out");
                return false;
            }
            return probe.Result;
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Windows Search provider is not usable");
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileHit> SearchAsync(string query, int limit, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var plan = FileSearchQuery.Plan(query, limit);
        if (plan == null || ct.IsCancellationRequested || !IsAvailable) yield break;

        var channel = Channel.CreateUnbounded<FileHit>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = Task.Factory.StartNew(
            () => Produce(plan, channel.Writer, cts.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = producer.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // never an unobserved exception

        try
        {
            await foreach (var hit in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return hit;
        }
        finally
        {
            // Consumer finished or went away: stop the query (cancels the command), then give the worker a moment to dispose reader + connection.
            cts.Cancel();
            await Task.WhenAny(producer, Task.Delay(500, CancellationToken.None)).ConfigureAwait(false);
        }
    }

    /// <summary>Worker: runs the plan's statements in order and writes de-duplicated hits. Never throws; always completes the channel.</summary>
    private void Produce(FileSearchPlan plan, ChannelWriter<FileHit> writer, CancellationToken ct)
    {
        try
        {
            using var conn = new OleDbConnection(ConnectionString);
            conn.Open();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int emitted = 0;

            foreach (var sql in plan.Statements)
            {
                if (emitted >= plan.Limit) break;
                ct.ThrowIfCancellationRequested();

                using var cmd = new OleDbCommand(sql, conn) { CommandTimeout = CommandTimeoutSeconds };
                // Cancelling the token aborts the running command so the worker does not sit in the provider after the UI moved on.
                using var registration = ct.Register(static state =>
                {
                    try { ((OleDbCommand)state!).Cancel(); }
                    catch (Exception) { /* provider may not support cancel; the per-row token check still ends the loop */ }
                }, cmd);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var hit = ReadRow(reader);
                    if (hit == null || !seen.Add(hit.Value.Path)) continue;
                    writer.TryWrite(hit.Value);
                    if (++emitted >= plan.Limit) break;
                }
            }
        }
        catch (OperationCanceledException) { /* normal: superseded or cancelled query */ }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) _log.LogWarning(ex, "Windows Search query failed");
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static FileHit? ReadRow(OleDbDataReader reader)
    {
        var path = reader.IsDBNull(0) ? null : reader.GetValue(0) as string;
        if (string.IsNullOrWhiteSpace(path) || FileSearchQuery.IsExcludedPath(path)) return null;

        uint attrs = 0;
        if (!reader.IsDBNull(3))
        {
            try { attrs = Convert.ToUInt32(reader.GetValue(3)); }
            catch (Exception) { /* unknown attributes: treat as a plain file */ }
        }
        if ((attrs & (FileSearchQuery.AttributeHidden | FileSearchQuery.AttributeSystem)) != 0) return null;

        var name = reader.IsDBNull(1) ? null : reader.GetValue(1) as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = path;
        }

        DateTimeOffset? modified = null;
        if (!reader.IsDBNull(2))
        {
            try
            {
                var dt = Convert.ToDateTime(reader.GetValue(2));
                // The index stores FILETIME (UTC). Unspecified is treated as UTC; Local is converted.
                modified = dt.Kind == DateTimeKind.Local ? new DateTimeOffset(dt) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            }
            catch (Exception) { /* leave null */ }
        }
        return new FileHit(path, name, (attrs & FileSearchQuery.AttributeDirectory) != 0, modified);
    }
}
