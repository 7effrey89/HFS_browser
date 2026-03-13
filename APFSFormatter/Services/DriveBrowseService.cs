using APFSFormatter.Models;

namespace APFSFormatter.Services;

/// <summary>
/// Attempts to browse the root of a mounted removable drive.
/// </summary>
public class DriveBrowseService
{
    private const string DefaultCopyDirectory = @"C:\Temp";
    private const int MaxRetryAttempts = 5;
    private const int RetryDelayMilliseconds = 1000;
    private const int MaxEntriesPerSection = 10;

    private readonly IDriveLetterResolver _driveLetterResolver;
    private readonly IFileSystemBrowser _fileSystemBrowser;
    private readonly IRawVolumeBrowser _rawVolumeBrowser;
    private readonly int _maxRetryAttempts;
    private readonly int _retryDelayMilliseconds;

    public DriveBrowseService(IDriveLetterResolver driveLetterResolver)
        : this(
            driveLetterResolver,
            new WindowsFileSystemBrowser(),
            new HfsPlusVolumeBrowser(),
            MaxRetryAttempts,
            RetryDelayMilliseconds)
    {
    }

    public DriveBrowseService(
        IDriveLetterResolver driveLetterResolver,
        IFileSystemBrowser fileSystemBrowser,
        IRawVolumeBrowser rawVolumeBrowser,
        int maxRetryAttempts,
        int retryDelayMilliseconds)
    {
        _driveLetterResolver = driveLetterResolver;
        _fileSystemBrowser = fileSystemBrowser;
        _rawVolumeBrowser = rawVolumeBrowser;
        _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
        _retryDelayMilliseconds = Math.Max(0, retryDelayMilliseconds);
    }

    public BrowseResult BrowseRoot(int diskIndex)
    {
        string driveLetter = WaitForDriveLetter(diskIndex);
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return BrowseResult.Fail(
                "Windows did not expose a drive letter for the selected USB volume.");
        }

        string rootPath = driveLetter + Path.DirectorySeparatorChar;

        try
        {
            IReadOnlyList<string> directories = _fileSystemBrowser.EnumerateDirectories(rootPath, MaxEntriesPerSection);
            IReadOnlyList<BrowseFileEntry> files = _fileSystemBrowser
                .EnumerateFiles(rootPath, MaxEntriesPerSection)
                .Select(fileName => new BrowseFileEntry { Name = fileName, CanCopy = true })
                .ToArray();

            string message = directories.Count == 0 && files.Count == 0
                ? $"Successfully opened {rootPath}, and the root directory is empty."
                : $"Successfully opened {rootPath} and enumerated the root directory.";

            return BrowseResult.Ok(
                message,
                BrowseSourceKind.WindowsMounted,
                driveLetter,
                rootPath,
                directories,
                files);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is DirectoryNotFoundException ||
            ex is NotSupportedException)
        {
            BrowseResult? rawBrowseResult = _rawVolumeBrowser.TryBrowseRoot(driveLetter);
            if (rawBrowseResult is not null)
                return rawBrowseResult;

            return BrowseResult.Fail(
                $"Windows exposed '{rootPath}', but the volume could not be browsed. {ex.Message}");
        }
    }

    public FileCopyResult CopyFileToTemp(BrowseResult browseResult, int fileIndex)
    {
        if (!browseResult.Success)
            return FileCopyResult.Fail("Copy is only available after a successful browse operation.");

        if (fileIndex < 0 || fileIndex >= browseResult.FileEntries.Count)
            return FileCopyResult.Fail("The selected file index is out of range.");

        BrowseFileEntry fileEntry = browseResult.FileEntries[fileIndex];
        if (!fileEntry.CanCopy)
        {
            return FileCopyResult.Fail(
                fileEntry.CopyUnavailableReason ?? "The selected file cannot be copied by this browser.");
        }

        Directory.CreateDirectory(DefaultCopyDirectory);

        if (browseResult.SourceKind == BrowseSourceKind.HfsPlusRaw)
        {
            FileCopyResult? rawCopyResult = _rawVolumeBrowser.TryCopyRootFile(
                browseResult.DriveLetter,
                fileEntry.Name,
                DefaultCopyDirectory);

            return rawCopyResult ?? FileCopyResult.Fail("The raw browser does not support file copying for this volume.");
        }

        if (string.IsNullOrWhiteSpace(browseResult.RootPath))
            return FileCopyResult.Fail("The browsed volume does not expose a usable root path.");

        string sourcePath = Path.Combine(browseResult.RootPath, fileEntry.Name);
        string destinationPath = Path.Combine(DefaultCopyDirectory, fileEntry.Name);

        try
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return FileCopyResult.Ok(
                $"Copied '{fileEntry.Name}' to '{destinationPath}'.",
                destinationPath);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is NotSupportedException)
        {
            return FileCopyResult.Fail(
                $"Failed to copy '{fileEntry.Name}' to '{destinationPath}'. {ex.Message}");
        }
    }

    private string WaitForDriveLetter(int diskIndex)
    {
        for (int attempt = 0; attempt < _maxRetryAttempts; attempt++)
        {
            string driveLetter = _driveLetterResolver.GetDriveLetterByDiskIndex(diskIndex);
            if (!string.IsNullOrWhiteSpace(driveLetter))
                return driveLetter;

            if (attempt < _maxRetryAttempts - 1 && _retryDelayMilliseconds > 0)
                Thread.Sleep(_retryDelayMilliseconds);
        }

        return string.Empty;
    }
}