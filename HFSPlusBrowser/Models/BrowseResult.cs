namespace HFSPlusBrowser.Models;

/// <summary>
/// Result of attempting to browse the root of a mounted drive.
/// </summary>
public class BrowseResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public BrowseSourceKind SourceKind { get; init; }

    public string DriveLetter { get; init; } = string.Empty;

    public string? RootPath { get; init; }

    public IReadOnlyList<string> Directories { get; init; } = Array.Empty<string>();

    public IReadOnlyList<BrowseFileEntry> FileEntries { get; init; } = Array.Empty<BrowseFileEntry>();

    public IReadOnlyList<string> Files => FileEntries.Select(file => file.Name).ToArray();

    public static BrowseResult Ok(
        string message,
        BrowseSourceKind sourceKind,
        string driveLetter,
        string rootPath,
        IReadOnlyList<string> directories,
        IReadOnlyList<BrowseFileEntry> fileEntries) =>
        new()
        {
            Success = true,
            Message = message,
            SourceKind = sourceKind,
            DriveLetter = driveLetter,
            RootPath = rootPath,
            Directories = directories,
            FileEntries = fileEntries
        };

    public static BrowseResult Fail(string message) =>
        new() { Success = false, Message = message };
}