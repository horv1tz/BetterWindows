using Microsoft.Extensions.Logging;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>Keeps every log line in memory so a failing real-Windows test can print the shell's own reasons (HRESULTs) in its assertion message.</summary>
internal sealed class CaptureLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = new();

    /// <summary>All captured lines, one per line, oldest first.</summary>
    public string Dump()
    {
        lock (_lines) return string.Join(Environment.NewLine, _lines);
    }

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    bool ILogger.IsEnabled(LogLevel logLevel) => true;

    void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = $"[{logLevel}] {formatter(state, exception)}" + (exception == null ? "" : " " + exception);
        lock (_lines) _lines.Add(text);
    }
}
