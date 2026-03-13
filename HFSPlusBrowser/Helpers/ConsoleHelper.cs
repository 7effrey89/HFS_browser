using HFSPlusBrowser.Models;

namespace HFSPlusBrowser.Helpers;

/// <summary>
/// Provides colored console output helpers for a consistent UI.
/// </summary>
public static class ConsoleHelper
{
    public static void WriteHeader()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║             HFS+ USB Browser for Windows             ║");
        Console.WriteLine("║      Browse and copy files on Apple HFS+ drives      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {message}");
        Console.ResetColor();
    }

    public static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {message}");
        Console.ResetColor();
    }

    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ {message}");
        Console.ResetColor();
    }

    public static void WriteStep(int step, int total, string description)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  [{step}/{total}] ");
        Console.ResetColor();
        Console.WriteLine(description);
    }

    public static void WriteProgress(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write($"  → {message}...");
        Console.ResetColor();
    }

    public static void WriteDone() => Console.WriteLine(" Done.");

    public static void WriteFail() => Console.WriteLine(" Failed.");

    public static void WriteDriveList(List<UsbDriveInfo> drives)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  Detected USB drives:");
        Console.ResetColor();

        for (int i = 0; i < drives.Count; i++)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"    [{i + 1}] ");
            Console.ResetColor();
            Console.WriteLine(drives[i].ToString());
        }

        Console.WriteLine();
    }

    public static void WriteSeparator()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('─', 56));
        Console.ResetColor();
    }

    public static void WriteListItem(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"    - {message}");
        Console.ResetColor();
    }

    public static void WriteIndexedListItem(int index, string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"    [{index}] {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Prompts the user to select a drive from the list.
    /// Returns the selected drive, or null if the user cancels.
    /// </summary>
    public static UsbDriveInfo? PromptDriveSelection(List<UsbDriveInfo> drives, string actionDescription)
    {
        while (true)
        {
            Console.Write($"  Enter the number of the drive to {actionDescription} (or 0 to exit): ");
            string? input = Console.ReadLine();

            if (input == "0" || string.IsNullOrWhiteSpace(input))
                return null;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= drives.Count)
                return drives[choice - 1];

            WriteWarning($"Invalid selection. Please enter a number between 1 and {drives.Count}.");
        }
    }

    public static bool PromptCopyFromBrowseResult()
    {
        Console.Write("  Copy one of the listed files to C:\\Temp? (y/N): ");
        string? input = Console.ReadLine()?.Trim();
        return string.Equals(input, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static bool PromptCopyFromTempToBrowseRoot()
    {
        Console.Write("  Copy a file from C:\\Temp to this drive root? (y/N): ");
        string? input = Console.ReadLine()?.Trim();
        return string.Equals(input, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static int? PromptFileSelection(IReadOnlyList<BrowseFileEntry> files)
    {
        while (true)
        {
            Console.Write("  Enter the number of the file to copy (or 0 to cancel): ");
            string? input = Console.ReadLine()?.Trim();

            if (input == "0" || string.IsNullOrWhiteSpace(input))
                return null;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= files.Count)
                return choice - 1;

            WriteWarning($"Invalid selection. Please enter a number between 1 and {files.Count}.");
        }
    }

    public static int? PromptNamedSelection(IReadOnlyList<string> items, string prompt)
    {
        while (true)
        {
            Console.Write($"  Enter the number of the {prompt} (or 0 to cancel): ");
            string? input = Console.ReadLine()?.Trim();

            if (input == "0" || string.IsNullOrWhiteSpace(input))
                return null;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= items.Count)
                return choice - 1;

            WriteWarning($"Invalid selection. Please enter a number between 1 and {items.Count}.");
        }
    }
}
