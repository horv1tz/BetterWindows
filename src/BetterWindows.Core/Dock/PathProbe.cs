namespace BetterWindows.Core.Dock;

public enum PathKind { Missing, File, Directory }

/// <summary>What exists at a (already validated, local) path. A seam so that Core tests do not depend on the host file system.</summary>
public interface IPathProbe
{
    PathKind Probe(string path);
}

/// <summary>Default <see cref="IPathProbe"/>: the real file system. Never throws.</summary>
public sealed class FileSystemPathProbe : IPathProbe
{
    public PathKind Probe(string path)
    {
        try
        {
            if (Directory.Exists(path)) return PathKind.Directory;
            if (File.Exists(path)) return PathKind.File;
        }
        catch (Exception) { /* inaccessible / invalid: treat as missing */ }
        return PathKind.Missing;
    }
}
