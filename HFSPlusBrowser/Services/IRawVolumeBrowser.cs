using HFSPlusBrowser.Models;

namespace HFSPlusBrowser.Services;

/// <summary>
/// Attempts to browse a volume using raw read-only filesystem parsing.
/// Returns null when the filesystem is not recognized by the implementation.
/// </summary>
public interface IRawVolumeBrowser
{
    BrowseResult? TryBrowseRoot(string driveLetter);

    FileCopyResult? TryCopyRootFile(string driveLetter, string fileName, string destinationDirectory);

    FileCopyResult? TryCopyFileToRoot(string driveLetter, string sourceFilePath);
}