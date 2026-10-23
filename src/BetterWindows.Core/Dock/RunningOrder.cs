namespace BetterWindows.Core.Dock;

/// <summary>
/// Remembers the order in which running apps were first seen, so that running-but-unpinned apps keep their dock position while the user switches windows
/// (the tracker's window order is MRU and would reshuffle the dock on every activation). An app that stops running is forgotten and, if it comes back,
/// appears at the end. Thread-safe.
/// </summary>
public sealed class RunningOrder
{
    private readonly object _gate = new();
    private readonly List<string> _order = new();
    private IReadOnlyList<string> _view = Array.Empty<string>();

    /// <summary>Feeds the app ids that currently have windows (any order; new ones are appended in the given order) and returns the stable order.</summary>
    public IReadOnlyList<string> Update(IEnumerable<string> runningAppIds)
    {
        var current = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in runningAppIds) if (seen.Add(id)) current.Add(id);
        lock (_gate)
        {
            var changed = _order.RemoveAll(id => !seen.Contains(id)) > 0;
            var known = new HashSet<string>(_order, StringComparer.OrdinalIgnoreCase);
            foreach (var id in current) if (known.Add(id)) { _order.Add(id); changed = true; }
            if (changed) _view = _order.ToArray();
            return _view;
        }
    }
}
