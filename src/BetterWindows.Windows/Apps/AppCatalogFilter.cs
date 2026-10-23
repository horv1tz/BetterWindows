using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>
/// Pure rules that turn raw start-menu items (from shell:AppsFolder or .lnk files) into <see cref="AppEntry"/> values and drop the noise that
/// the Start menu lists but that is not an application: uninstallers, readme/help/licence links, web links and shortcuts to documents.
/// </summary>
internal static partial class AppCatalogFilter
{
    /// <summary>Extensions that can be started as a program ("launchable target"); everything else (.txt, .chm, .pdf, .url, .html...) is a document link.</summary>
    private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".msc", ".cpl",
    };

    public static bool IsLaunchableTarget(string? path)
        => !string.IsNullOrEmpty(path) && LaunchableExtensions.Contains(AppIdentity.ExtensionOf(path));

    /// <summary>
    /// Builds an entry or returns null when the item must not be listed.
    /// </summary>
    /// <param name="aumid">System.AppUserModel.ID when known (explicit or shell-synthesised).</param>
    /// <param name="name">Display name as the Start menu shows it.</param>
    /// <param name="targetPath">Resolved target of the shortcut (System.Link.TargetParsingPath), may contain %VARS%/be relative; null for packaged apps.</param>
    /// <param name="packagedHint">The item reports a package identity (PackageFullName/FamilyName).</param>
    public static AppEntry? TryBuild(string? aumid, string? name, string? targetPath, bool packagedHint = false)
    {
        var cleanAumid = AppIdentity.IsValidAumid(aumid) ? aumid!.Trim() : null;
        var path = AppIdentity.NormalizeWindowsPath(ExpandVariables(targetPath));

        // A target that is not a program (readme.txt, help.chm, "Visit website.url", folder shortcut) is not an app.
        if (path != null && !IsLaunchableTarget(path)) return null;

        var displayName = (name ?? "").Trim();
        if (displayName.Length == 0 && path != null)
        {
            var file = AppIdentity.FileNameOf(path);
            var dot = file.LastIndexOf('.');
            displayName = dot > 0 ? file[..dot] : file;
        }
        if (displayName.Length == 0) return null;
        if (displayName.Length > 256) displayName = displayName[..256];

        // Nothing to launch with: neither an AUMID the shell can start nor an exe path.
        if (cleanAumid == null && path == null) return null;

        if (IsNoise(displayName, path)) return null;

        // "Packaged" only means something together with the AUMID a packaged app is launched by.
        var packaged = cleanAumid != null && (packagedHint || AppIdentity.IsPackagedAumid(cleanAumid));
        return new AppEntry
        {
            Id = cleanAumid != null ? AppIdentity.IdFromAumid(cleanAumid) : AppIdentity.IdFromExePath(path!),
            Name = displayName,
            Path = path,
            Aumid = cleanAumid,
            Packaged = packaged,
            IconUrl = "", // Core fills this in (DynUrls.AppIcon)
            Category = null,
        };
    }

    /// <summary>True for uninstallers and documentation links that the Start menu lists next to real apps.</summary>
    public static bool IsNoise(string name, string? normalizedTargetPath)
    {
        if (normalizedTargetPath != null)
        {
            var file = AppIdentity.FileNameOf(normalizedTargetPath);
            if (UninstallerExe().IsMatch(file)) return true;
            // "Uninstall X" shortcuts frequently point at msiexec /x{GUID}; the shortcut itself is the noise, msiexec is never an app.
            if (file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (UninstallName().IsMatch(name)) return true;
        // Documentation shortcuts that happen to point at an exe (viewers): only the unambiguous words, "Help"/"Manual" alone could be a real app.
        if (DocumentationName().IsMatch(name)) return true;
        return false;
    }

    private static string? ExpandVariables(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return path.Contains('%') ? Environment.ExpandEnvironmentVariables(path) : path; }
        catch (Exception) { return path; }
    }

    // unins000.exe (Inno Setup), uninst.exe / uninstall.exe (NSIS & co), "Uninstall.exe", "uninstaller.exe", "unwise.exe".
    [GeneratedRegex(@"^(unins\d*|uninst(all(er)?)?|unwise(32)?|.*_uninstall.*)\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UninstallerExe();

    [GeneratedRegex(@"^\s*(uninstall|un-install|удалить|удаление|деинсталляция|деинсталлировать)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UninstallName();

    [GeneratedRegex(@"^\s*(readme|read me|release notes|license( agreement)?|licence|eula|changelog|what'?s new|visit (our )?(web ?site|home ?page)|прочти меня|лицензионное соглашение|примечания к выпуску)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentationName();
}
