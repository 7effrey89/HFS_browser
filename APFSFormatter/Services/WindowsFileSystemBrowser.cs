namespace APFSFormatter.Services;

/// <summary>
/// Uses System.IO APIs to enumerate a mounted Windows filesystem path.
/// </summary>
public class WindowsFileSystemBrowser : IFileSystemBrowser
{
    public IReadOnlyList<string> EnumerateDirectories(string rootPath, int maxEntries) =>
        Directory.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(maxEntries)
            .Cast<string>()
            .ToArray();

    public IReadOnlyList<string> EnumerateFiles(string rootPath, int maxEntries) =>
        Directory.EnumerateFiles(rootPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(maxEntries)
            .Cast<string>()
            .ToArray();
}