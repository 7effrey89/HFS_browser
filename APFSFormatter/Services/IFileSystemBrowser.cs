namespace APFSFormatter.Services;

/// <summary>
/// Enumerates files and directories for a mounted filesystem path.
/// </summary>
public interface IFileSystemBrowser
{
    IReadOnlyList<string> EnumerateDirectories(string rootPath, int maxEntries);

    IReadOnlyList<string> EnumerateFiles(string rootPath, int maxEntries);
}