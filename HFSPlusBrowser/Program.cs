using HFSPlusBrowser.Helpers;
using HFSPlusBrowser.Models;
using HFSPlusBrowser.Services;

namespace HFSPlusBrowser;

/// <summary>
/// Entry point for the HFS+ USB Browser console application.
/// Guides the user through detecting a removable drive and browsing or copying
/// files from HFS+/HFSX volumes on Windows.
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
        ConsoleHelper.WriteStep(1, 2, "Scanning for removable USB drives...");
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

        return RunBrowseMode(drives, detectionService);
    }

    private static int RunBrowseMode(
        List<UsbDriveInfo> drives,
        DriveDetectionService detectionService)
    {
        ConsoleHelper.WriteStep(2, 2, "Select the USB drive to browse:");
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
        ConsoleHelper.WriteInfo("    Raw HFS+ browsing and copy operations may require Administrator privileges when Windows does not mount the volume normally.");
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
