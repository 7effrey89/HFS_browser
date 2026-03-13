using APFSFormatter.Helpers;
using APFSFormatter.Models;
using APFSFormatter.Services;

namespace APFSFormatter;

/// <summary>
/// Entry point for the APFS USB Formatter console application.
/// Guides the user through detecting, selecting, and formatting a USB drive
/// with the Apple APFS (Apple File System) format on Windows.
/// </summary>
internal class Program
{
    private static int Main(string[] args)
    {
        ConsoleHelper.WriteHeader();

        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"An unexpected error occurred: {ex.Message}");
            Console.WriteLine();
            PauseAndExit();
            return 1;
        }
    }

    private static int Run()
    {
        ConsoleHelper.WriteStep(1, 3, "Choose an operation mode...");
        Console.WriteLine();

        ConsoleHelper.OperationMode? mode = ConsoleHelper.PromptOperationMode();
        if (mode is null)
        {
            ConsoleHelper.WriteInfo("Operation cancelled.");
            Console.WriteLine();
            return 0;
        }

        if (mode == ConsoleHelper.OperationMode.Format && !IsRunningAsAdministrator())
        {
            ConsoleHelper.WriteError("Formatting must be run as Administrator.");
            ConsoleHelper.WriteInfo("  Right-click the executable and choose 'Run as administrator'.");
            Console.WriteLine();
            PauseAndExit();
            return 1;
        }

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        ConsoleHelper.WriteStep(2, 3, "Scanning for removable USB drives...");
        Console.WriteLine();

        var detectionService = new DriveDetectionService();
        List<UsbDriveInfo> drives;

        try
        {
            drives = detectionService.GetRemovableDrives();
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"Failed to enumerate drives: {ex.Message}");
            PauseAndExit();
            return 1;
        }

        if (drives.Count == 0)
        {
            ConsoleHelper.WriteWarning("No removable USB drives were found.");
            ConsoleHelper.WriteInfo("  Please connect a USB drive and try again.");
            Console.WriteLine();
            PauseAndExit();
            return 0;
        }

        ConsoleHelper.WriteDriveList(drives);
        ConsoleHelper.WriteSeparator();

        return mode == ConsoleHelper.OperationMode.Browse
            ? RunBrowseMode(drives, detectionService)
            : RunFormatMode(drives, detectionService);
    }

    private static int RunBrowseMode(
        List<UsbDriveInfo> drives,
        DriveDetectionService detectionService)
    {
        ConsoleHelper.WriteStep(3, 3, "Select the USB drive to browse:");
        Console.WriteLine();

        UsbDriveInfo? selectedDrive = ConsoleHelper.PromptDriveSelection(drives, "browse");
        if (selectedDrive is null)
        {
            ConsoleHelper.WriteInfo("Operation cancelled.");
            Console.WriteLine();
            return 0;
        }

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        ConsoleHelper.WriteInfo("  Browse result:");

        var browseService = new DriveBrowseService(detectionService);
        BrowseResult browseResult = browseService.BrowseRoot(selectedDrive.DiskIndex);
        WriteBrowseResult(browseResult);

        MaybeCopyBrowsedFile(browseService, browseResult);
        MaybeCopyTempFileToBrowseRoot(browseService, browseResult);

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        PauseAndExit();
        return 0;
    }

    private static int RunFormatMode(
        List<UsbDriveInfo> drives,
        DriveDetectionService detectionService)
    {
        ConsoleHelper.WriteStep(3, 3, "Select the USB drive to format as APFS:");
        Console.WriteLine();

        UsbDriveInfo? selectedDrive = ConsoleHelper.PromptDriveSelection(drives, "format");
        if (selectedDrive is null)
        {
            ConsoleHelper.WriteInfo("Operation cancelled.");
            Console.WriteLine();
            return 0;
        }

        string volumeLabel = ConsoleHelper.PromptVolumeLabel();

        if (!ConsoleHelper.ConfirmDestructiveAction(selectedDrive))
        {
            ConsoleHelper.WriteInfo("Operation cancelled -- no changes were made.");
            Console.WriteLine();
            PauseAndExit();
            return 0;
        }

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();

        ConsoleHelper.WriteInfo("  Formatting drive as APFS...");
        Console.WriteLine();

        return FormatDrive(selectedDrive, volumeLabel, detectionService);
    }

    private static int FormatDrive(
        UsbDriveInfo drive,
        string volumeLabel,
        DriveDetectionService detectionService)
    {
        const int totalSubSteps = 2;

        // Sub-step 1: Prepare disk with diskpart (clean, GPT, APFS partition type GUID)
        ConsoleHelper.WriteProgress($"  [1/{totalSubSteps}] Cleaning disk and creating GPT partition");

        var diskPartService = new DiskPartService();
        FormatResult partitionResult = diskPartService.PrepareForApfs(drive.DiskIndex, volumeLabel);

        if (!partitionResult.Success)
        {
            ConsoleHelper.WriteFail();
            Console.WriteLine();
            ConsoleHelper.WriteError($"Partition step failed: {partitionResult.Message}");

            if (!string.IsNullOrEmpty(partitionResult.DiskPartOutput))
            {
                Console.WriteLine();
                ConsoleHelper.WriteInfo("diskpart output:");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(partitionResult.DiskPartOutput);
                Console.ResetColor();
            }

            PauseAndExit();
            return 1;
        }

        ConsoleHelper.WriteDone();

        // Sub-step 2: Write APFS container superblock
        ConsoleHelper.WriteProgress($"  [2/{totalSubSteps}] Writing APFS container superblock");

        var apfsWriter = new ApfsContainerWriter();
        FormatResult superblockResult = apfsWriter.WriteContainerSuperblock(
            drive.DiskIndex, drive.SizeBytes);

        if (!superblockResult.Success)
        {
            ConsoleHelper.WriteFail();
            Console.WriteLine();
            ConsoleHelper.WriteError($"APFS superblock step failed: {superblockResult.Message}");
            ConsoleHelper.WriteWarning(
                "The drive has been partitioned with the APFS partition type GUID, " +
                "but the APFS file system structures could not be written.");
            PauseAndExit();
            return 1;
        }

        ConsoleHelper.WriteDone();

        // Success!
        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        ConsoleHelper.WriteSuccess("USB drive successfully formatted as APFS!");
        Console.WriteLine();
        ConsoleHelper.WriteInfo("  Summary:");
        ConsoleHelper.WriteInfo($"    Drive : {drive}");
        ConsoleHelper.WriteInfo($"    Format: APFS (Apple File System)");
        ConsoleHelper.WriteInfo($"    Layout: GPT with APFS container partition");
        Console.WriteLine();
        ConsoleHelper.WriteInfo("  Notes:");
        ConsoleHelper.WriteInfo("    * This tool writes a minimal APFS container seed.");
        ConsoleHelper.WriteInfo("    * It does not implement the full APFS metadata needed for");
        ConsoleHelper.WriteInfo("      native Windows browsing of the filesystem root.");
        ConsoleHelper.WriteInfo("    * Windows cannot natively read or write APFS drives.");
        ConsoleHelper.WriteInfo("      Use a third-party driver (e.g., Paragon APFS) for");
        ConsoleHelper.WriteInfo("      Windows access, or initialize and inspect the drive on macOS.");

        Console.WriteLine();
        ConsoleHelper.WriteInfo("  Browse check:");
        var browseService = new DriveBrowseService(detectionService);
        WriteBrowseResult(browseService.BrowseRoot(drive.DiskIndex));

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        PauseAndExit();
        return 0;
    }

    private static void WriteBrowseResult(BrowseResult browseResult)
    {
        if (browseResult.Success)
        {
            ConsoleHelper.WriteSuccess(browseResult.Message);

            if (browseResult.Directories.Count > 0)
            {
                ConsoleHelper.WriteInfo("    Directories:");
                foreach (string directory in browseResult.Directories)
                    ConsoleHelper.WriteListItem(directory);
            }

            if (browseResult.Files.Count > 0)
            {
                ConsoleHelper.WriteInfo("    Files:");
                for (int index = 0; index < browseResult.FileEntries.Count; index++)
                {
                    BrowseFileEntry file = browseResult.FileEntries[index];
                    string label = file.CanCopy
                        ? file.Name
                        : $"{file.Name} (copy unavailable: {file.CopyUnavailableReason})";
                    ConsoleHelper.WriteIndexedListItem(index + 1, label);
                }
            }

            if (browseResult.Directories.Count == 0 && browseResult.Files.Count == 0)
                ConsoleHelper.WriteInfo("    No files or directories were found at the root.");

            return;
        }

        ConsoleHelper.WriteWarning(browseResult.Message);
        ConsoleHelper.WriteInfo("    Stock Windows cannot browse APFS. A third-party APFS driver or macOS is required.");
    }

    private static void MaybeCopyBrowsedFile(DriveBrowseService browseService, BrowseResult browseResult)
    {
        if (!browseResult.Success || browseResult.FileEntries.Count == 0)
            return;

        if (!ConsoleHelper.PromptCopyFromBrowseResult())
            return;

        int? selectedIndex = ConsoleHelper.PromptFileSelection(browseResult.FileEntries);
        if (selectedIndex is null)
        {
            ConsoleHelper.WriteInfo("Copy cancelled.");
            return;
        }

        FileCopyResult copyResult = browseService.CopyFileToTemp(browseResult, selectedIndex.Value);
        if (copyResult.Success)
        {
            ConsoleHelper.WriteSuccess(copyResult.Message);
        }
        else
        {
            ConsoleHelper.WriteWarning(copyResult.Message);
        }
    }

    private static void MaybeCopyTempFileToBrowseRoot(DriveBrowseService browseService, BrowseResult browseResult)
    {
        if (!browseResult.Success)
            return;

        if (!ConsoleHelper.PromptCopyFromTempToBrowseRoot())
            return;

        IReadOnlyList<string> tempFiles = browseService.GetImportableTempFiles(browseResult);
        if (tempFiles.Count == 0)
        {
            if (browseResult.SourceKind == BrowseSourceKind.HfsPlusRaw)
            {
                ConsoleHelper.WriteInfo(
                    "No compatible same-named files from C:\\Temp were found for raw HFS+ overwrite.");
            }
            else
            {
                ConsoleHelper.WriteInfo("No files were found in C:\\Temp.");
            }

            return;
        }

        ConsoleHelper.WriteInfo(
            browseResult.SourceKind == BrowseSourceKind.HfsPlusRaw
                ? "    Compatible C:\\Temp files:"
                : "    C:\\Temp files:");
        for (int index = 0; index < tempFiles.Count; index++)
            ConsoleHelper.WriteIndexedListItem(index + 1, tempFiles[index]);

        if (browseResult.SourceKind == BrowseSourceKind.HfsPlusRaw)
        {
            ConsoleHelper.WriteInfo(
                "    Raw HFS+ import currently overwrites an existing same-named root file when its inline extents already have enough space.");
        }

        int? selectedIndex = ConsoleHelper.PromptNamedSelection(tempFiles, "C:\\Temp file to copy");
        if (selectedIndex is null)
        {
            ConsoleHelper.WriteInfo("Copy cancelled.");
            return;
        }

        FileCopyResult copyResult = browseService.CopyFileFromTempToRoot(browseResult, selectedIndex.Value);
        if (copyResult.Success)
        {
            ConsoleHelper.WriteSuccess(copyResult.Message);
        }
        else
        {
            ConsoleHelper.WriteWarning(copyResult.Message);
        }
    }

    /// <summary>
    /// Checks whether the current process is running with administrator privileges.
    /// </summary>
    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(
                System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we cannot determine, assume not admin
            return false;
        }
    }

    private static void PauseAndExit()
    {
        Console.WriteLine("Press any key to exit...");

        try
        {
            if (!Console.IsInputRedirected)
                Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // Ignore pause failures when the process is run with redirected input.
        }
    }
}
