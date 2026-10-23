using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BetterWindows.Host.Logging;

/// <summary>Serilog configuration of the Host: a daily rolling file (7 kept) plus the debugger output window. Everything else logs through Microsoft.Extensions.Logging into this.</summary>
public static class HostLogging
{
    /// <summary>File name pattern: <c>bw-20261023.log</c>.</summary>
    public const string FilePattern = "bw-.log";
    public const int RetainedFiles = 7;

    /// <summary>
    /// Creates the process logger. <paramref name="shared"/> lets the guardian / smoke / diagnose processes write the same day file as the shell. A log directory that cannot
    /// be created never stops the app: the logger then only writes to the debugger.
    /// </summary>
    public static Serilog.Core.Logger Create(string logsDir, bool debugLevel, bool shared = true)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Is(debugLevel ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Sink(new DebuggerSink());
        try
        {
            Directory.CreateDirectory(logsDir);
            config = config.WriteTo.File(
                Path.Combine(logsDir, FilePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles,
                shared: shared,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"BetterWindows: file logging disabled: {ex.Message}");
        }
        return config.CreateLogger();
    }
}

/// <summary>Writes every event to the attached debugger / DebugView via <see cref="Trace"/> (cheap when nobody listens).</summary>
internal sealed class DebuggerSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        try { Trace.WriteLine($"[{logEvent.Level}] {logEvent.RenderMessage()}{(logEvent.Exception is null ? "" : " :: " + logEvent.Exception.Message)}", "BetterWindows"); }
        catch (Exception) { /* a logging sink must never throw */ }
    }
}
