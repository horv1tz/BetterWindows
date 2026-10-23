using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Native;

namespace BetterWindows.Host.Cli;

/// <summary>Process exit codes (docs/IPC.md section 15). Stable: CI and the installer rely on them.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Failed = 1;
    /// <summary>WebView2 Evergreen runtime is missing (the shell was not started, the taskbar untouched).</summary>
    public const int RuntimeMissing = 2;
    /// <summary>Started elevated: refused.</summary>
    public const int Elevated = 3;
    /// <summary>Smoke test: the surfaces did not become ready in time or delivered nothing.</summary>
    public const int SmokeFailed = 4;
}

/// <summary>
/// The non-shell command modes that run WITHOUT the WPF application, the single-instance mutex or any surface: <c>--guardian</c>, <c>--restore-taskbar</c> and
/// <c>--diagnose</c>. Each returns an exit code; output goes to the parent console (or <c>--out</c>).
/// </summary>
public static class CliCommands
{
    /// <summary>Waits for the owner to exit and restores the shell (docs/IPC.md section 15, guardian).</summary>
    public static int RunGuardian(CommandLineOptions cli, IAppPaths paths) => GuardianRunner.Run(cli.GuardianOwnerPid, paths.LogsDir);

    /// <summary>
    /// <c>--restore-taskbar</c>: asks the live shell to quit (it restores everything on its way out), waits up to 5 s, then restores the taskbar and desktop icons
    /// itself. Always restores; never starts the shell.
    /// </summary>
    public static int RestoreTaskbar(TimeSpan? ownerWait = null)
    {
        var wait = ownerWait ?? TimeSpan.FromSeconds(5);
        var sw = Stopwatch.StartNew();
        if (SingleInstance.SignalQuit())
        {
            while (SingleInstance.IsRunning() && sw.Elapsed < wait) Thread.Sleep(200);
        }
        try { TaskbarRestorer.RestoreNow(); } catch (Exception) { return ExitCodes.Failed; }
        try { DesktopIconsRestorer.RestoreNow(); } catch (Exception) { return ExitCodes.Failed; }
        return ExitCodes.Ok;
    }

    /// <summary>
    /// <c>--diagnose [--json] [--out file]</c>: read-only self-check (docs/IPC.md section 15). It does not create the crash counter, does not touch journals and does not
    /// start WebView2.
    /// </summary>
    public static int Diagnose(CommandLineOptions cli, IAppPaths paths, Func<string?> webView2Version, IReadOnlyList<RestoreJournal> journals, bool elevated, bool indexPresent)
    {
        var report = DiagnosticsReport.Build(cli, paths, webView2Version(), journals, elevated, indexPresent);
        var text = cli.Json ? JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) : DiagnosticsReport.ToText(report);
        WriteOutput(text, cli.OutputFile);
        return ExitCodes.Ok;
    }

    /// <summary>Writes to <c>--out</c> when given, otherwise to the parent console (silently nowhere when there is none).</summary>
    public static void WriteOutput(string text, string? outputFile)
    {
        if (!string.IsNullOrEmpty(outputFile))
        {
            try { File.WriteAllText(outputFile, text + Environment.NewLine, new UTF8Encoding(false)); return; }
            catch (Exception) { /* fall back to the console */ }
        }
        using var console = NativeHelpers.OpenConsoleWriter();
        console.WriteLine(text);
    }
}

/// <summary>The data of <c>--diagnose</c>. Plain values only: the same object is printed as text or JSON.</summary>
public sealed record DiagnosticsSnapshot(
    string Version,
    string Build,
    string Os,
    string WebView2,
    bool Elevated,
    bool BundlePresent,
    string DataDir,
    int StaleJournals,
    int ProtocolVersion,
    IReadOnlyList<string> CommandLineErrors);

/// <summary>Builds <see cref="DiagnosticsSnapshot"/> from the environment (pure except for the injected values).</summary>
public static class DiagnosticsReport
{
    public static DiagnosticsSnapshot Build(CommandLineOptions cli, IAppPaths paths, string? webView2, IReadOnlyList<RestoreJournal> journals, bool elevated, bool indexPresent) =>
        new(
            Version: BuildMetadata.Version,
            Build: BuildMetadata.BuildInfo,
            Os: BuildMetadata.OsDescription(),
            WebView2: string.IsNullOrEmpty(webView2) ? "not installed" : webView2,
            Elevated: elevated,
            BundlePresent: indexPresent,
            DataDir: paths.Root,
            StaleJournals: journals.Count,
            ProtocolVersion: ProtocolInfo.Version,
            CommandLineErrors: cli.Errors);

    public static string ToText(DiagnosticsSnapshot s)
    {
        var sb = new StringBuilder();
        void Line(string name, string value) => sb.Append(name.PadRight(18)).Append(value).Append(Environment.NewLine);
        Line("BetterWindows", $"{s.Version} ({s.Build})");
        Line("Protocol", s.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Line("Windows", s.Os);
        Line("WebView2", s.WebView2);
        Line("Elevated", s.Elevated ? "yes (refused by the shell)" : "no");
        Line("UI bundle", s.BundlePresent ? "present" : "MISSING (wwwroot/index.html)");
        Line("Data directory", s.DataDir);
        Line("Stale journals", s.StaleJournals.ToString(CultureInfo.InvariantCulture));
        foreach (var e in s.CommandLineErrors) Line("Argument error", e);
        return sb.ToString().TrimEnd();
    }
}
