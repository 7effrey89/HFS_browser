using APFSFormatter.Models;

namespace APFSFormatter.Helpers;

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
        Console.WriteLine("║           APFS USB Formatter for Windows             ║");
        Console.WriteLine("║     Format your USB drive with Apple APFS format     ║");
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

    /// <summary>
    /// Prompts the user to confirm a destructive operation by typing the exact drive label.
    /// </summary>
    public static bool ConfirmDestructiveAction(UsbDriveInfo drive)
    {
        Console.WriteLine();
        WriteWarning("WARNING: This will PERMANENTLY ERASE all data on the selected drive!");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Drive: {drive}");
        Console.ResetColor();
        Console.WriteLine();
        Console.Write("  Type ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("YES");
        Console.ResetColor();
        Console.Write(" to confirm and proceed: ");

        string? input = Console.ReadLine();
        return string.Equals(input?.Trim(), "YES", StringComparison.Ordinal);
    }

    /// <summary>
    /// Prompts the user to select a drive from the list.
    /// Returns the selected drive, or null if the user cancels.
    /// </summary>
    public static UsbDriveInfo? PromptDriveSelection(List<UsbDriveInfo> drives)
    {
        while (true)
        {
            Console.Write("  Enter the number of the drive to format (or 0 to exit): ");
            string? input = Console.ReadLine();

            if (input == "0" || string.IsNullOrWhiteSpace(input))
                return null;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= drives.Count)
                return drives[choice - 1];

            WriteWarning($"Invalid selection. Please enter a number between 1 and {drives.Count}.");
        }
    }

    /// <summary>
    /// Prompts the user to enter an optional volume label.
    /// Returns "APFS" if the user presses Enter without typing anything.
    /// </summary>
    public static string PromptVolumeLabel()
    {
        Console.Write("  Enter volume label (default: APFS): ");
        string? label = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(label) ? "APFS" : label;
    }

    /// <summary>
    /// Prompts the user whether to create a dummy text file after formatting.
    /// </summary>
    public static bool PromptCreateDummyFile()
    {
        Console.Write("  Create a dummy text file on the formatted drive if Windows can access it? (y/N): ");
        string? input = Console.ReadLine()?.Trim();
        return string.Equals(input, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
