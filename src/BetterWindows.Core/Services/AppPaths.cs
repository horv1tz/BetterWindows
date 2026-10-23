namespace BetterWindows.Core.Services;

/// <summary>Default <see cref="IAppPaths"/>: %LOCALAPPDATA%\BetterWindows, overridable by BETTERWINDOWS_DATA_DIR (the Host maps --data-dir to it).</summary>
public sealed class AppPaths : IAppPaths
{
    public const string EnvVar = "BETTERWINDOWS_DATA_DIR";

    public AppPaths() : this(Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } env ? env
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWindows")) { }

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        ConfigFile = Path.Combine(Root, "config.json");
        DataDir = Path.Combine(Root, "data");
        StateDir = Path.Combine(Root, "state");
        LogsDir = Path.Combine(Root, "logs");
        CacheDir = Path.Combine(Root, "cache");
        WebView2Dir = Path.Combine(Root, "WebView2");
    }

    public string Root { get; }
    public string ConfigFile { get; }
    public string DataDir { get; }
    public string StateDir { get; }
    public string LogsDir { get; }
    public string CacheDir { get; }
    public string WebView2Dir { get; }
}
