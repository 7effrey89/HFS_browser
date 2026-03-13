namespace HFSPlusBrowser.Models;

public class BrowseFileEntry
{
    public string Name { get; init; } = string.Empty;

    public bool CanCopy { get; init; }

    public string? CopyUnavailableReason { get; init; }
}