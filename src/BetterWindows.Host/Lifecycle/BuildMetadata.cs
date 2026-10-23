using System.Reflection;
using System.Runtime.InteropServices;

namespace BetterWindows.Host.Lifecycle;

/// <summary>Version, git revision and OS description of this build, derived from assembly metadata (feeds <c>IAppLifecycle</c> and <c>system.about</c>).</summary>
public static class BuildMetadata
{
    private static readonly Lazy<(string Version, string Sha)> Info = new(() =>
    {
        var asm = typeof(BuildMetadata).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var explicitSha = asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key is "GitSha" or "SourceRevisionId")?.Value;
        var (version, sha) = ParseInformationalVersion(informational);
        return (version ?? asm.GetName().Version?.ToString(3) ?? "0.0.0", ShortSha(explicitSha) ?? sha ?? "");
    });

    /// <summary>"0.1.0" (without the +sha build metadata).</summary>
    public static string Version => Info.Value.Version;
    /// <summary>First 8 characters of the git revision, or "" when the build has none.</summary>
    public static string GitSha => Info.Value.Sha;

    /// <summary>"9f2c1a7d" or "9f2c1a7d, debug" / "dev, debug".</summary>
    public static string BuildInfo
    {
        get
        {
#if DEBUG
            const string config = ", debug";
#else
            const string config = "";
#endif
            return (GitSha.Length > 0 ? GitSha : "dev") + config;
        }
    }

    /// <summary>"Microsoft Windows 10.0.22631 (build 22631)".</summary>
    public static string OsDescription() => DescribeOs(RuntimeInformation.OSDescription, Environment.OSVersion.Version);

    /// <summary>Splits "1.2.3+abcdef0123" into ("1.2.3", "abcdef01"). Missing or malformed input yields nulls.</summary>
    public static (string? Version, string? Sha) ParseInformationalVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return (null, null);
        var plus = informational.IndexOf('+');
        var version = (plus >= 0 ? informational[..plus] : informational).Trim();
        var sha = plus >= 0 ? ShortSha(informational[(plus + 1)..].Split('.', '+', '-')[0]) : null;
        return (version.Length == 0 ? null : version, sha);
    }

    /// <summary>OS text plus the Windows build number (the part support asks for).</summary>
    public static string DescribeOs(string osDescription, Version version)
    {
        var text = string.IsNullOrWhiteSpace(osDescription) ? "Windows" : osDescription.Trim();
        return version.Build > 0 && !text.Contains($"build {version.Build}", StringComparison.OrdinalIgnoreCase) ? $"{text} (build {version.Build})" : text;
    }

    private static string? ShortSha(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        foreach (var c in s) if (!Uri.IsHexDigit(c)) return null;
        return s.Length <= 8 ? s : s[..8];
    }
}
