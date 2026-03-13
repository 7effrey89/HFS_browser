using System.Text;
using APFSFormatter.Models;

namespace APFSFormatter.Services;

/// <summary>
/// Creates a dummy text file on a removable drive when Windows exposes a writable drive letter.
/// </summary>
public class DummyFileService
{
    private const int MaxRetryAttempts = 5;
    private const int RetryDelayMilliseconds = 1000;

    private readonly DriveDetectionService _driveDetectionService;

    public DummyFileService(DriveDetectionService driveDetectionService)
    {
        _driveDetectionService = driveDetectionService;
    }

    /// <summary>
    /// Attempts to create a dummy text file on the specified removable disk.
    /// This only succeeds when Windows can mount the volume and provide a writable drive letter.
    /// </summary>
    public FormatResult CreateDummyFile(int diskIndex, string volumeLabel)
    {
        string driveLetter = WaitForDriveLetter(diskIndex);
        if (string.IsNullOrEmpty(driveLetter))
        {
            return FormatResult.Fail(
                "Windows did not expose a writable drive letter for the formatted APFS volume. " +
                "Connect the drive to macOS or install an APFS driver for Windows to create files on it.");
        }

        string rootPath = driveLetter + Path.DirectorySeparatorChar;
        string filePath = Path.Combine(rootPath, "dummy.txt");
        string content = BuildDummyFileContents(volumeLabel, diskIndex, driveLetter);

        try
        {
            File.WriteAllText(filePath, content, Encoding.UTF8);
            return FormatResult.Ok($"Dummy file created successfully at '{filePath}'.");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
        {
            return FormatResult.Fail(
                $"Windows located the drive at '{rootPath}', but could not create the dummy file. {ex.Message}");
        }
    }

    private string WaitForDriveLetter(int diskIndex)
    {
        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            string driveLetter = _driveDetectionService.GetDriveLetterByDiskIndex(diskIndex);
            if (!string.IsNullOrWhiteSpace(driveLetter))
                return driveLetter;

            Thread.Sleep(RetryDelayMilliseconds);
        }

        return string.Empty;
    }

    private static string BuildDummyFileContents(string volumeLabel, int diskIndex, string driveLetter)
    {
        return string.Join(Environment.NewLine,
        [
            "APFS USB Formatter dummy file",
            $"Volume label: {volumeLabel}",
            $"Disk index: {diskIndex}",
            $"Drive letter: {driveLetter}",
            $"Created: {DateTimeOffset.Now:O}",
            string.Empty,
            "If you can read this file, Windows currently has access to the formatted drive."
        ]);
    }
}
