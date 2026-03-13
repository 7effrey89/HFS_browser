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

        // Check for administrator privileges (required for disk operations)
        if (!IsRunningAsAdministrator())
        {
            ConsoleHelper.WriteError("This application must be run as Administrator.");
            ConsoleHelper.WriteInfo("  Right-click the executable and choose 'Run as administrator'.");
            Console.WriteLine();
            PauseAndExit();
            return 1;
        }

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
        // Step 1: Detect USB drives
        ConsoleHelper.WriteStep(1, 3, "Scanning for removable USB drives...");
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

        // Step 2: Drive selection and confirmation
        ConsoleHelper.WriteStep(2, 3, "Select the USB drive to format as APFS:");
        Console.WriteLine();

        UsbDriveInfo? selectedDrive = ConsoleHelper.PromptDriveSelection(drives);
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

        // Step 3: Format the drive
        ConsoleHelper.WriteStep(3, 3, "Formatting drive as APFS...");
        Console.WriteLine();

        return FormatDrive(selectedDrive, volumeLabel);
    }

    private static int FormatDrive(
        UsbDriveInfo drive,
        string volumeLabel)
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
        ConsoleHelper.WriteInfo("    * The drive is now ready for use with macOS.");
        ConsoleHelper.WriteInfo("    * macOS will complete the APFS volume initialization");
        ConsoleHelper.WriteInfo("      the first time the drive is mounted.");
        ConsoleHelper.WriteInfo("    * Windows cannot natively read or write APFS drives.");
        ConsoleHelper.WriteInfo("      Use a third-party driver (e.g., Paragon APFS) for");
        ConsoleHelper.WriteInfo("      Windows access, or use the drive exclusively on Mac.");

        Console.WriteLine();
        ConsoleHelper.WriteSeparator();
        PauseAndExit();
        return 0;
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
        Console.ReadKey(intercept: true);
    }
}
