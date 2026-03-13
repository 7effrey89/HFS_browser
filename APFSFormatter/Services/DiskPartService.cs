using System.Diagnostics;
using System.Text;
using APFSFormatter.Models;

namespace APFSFormatter.Services;

/// <summary>
/// Executes diskpart commands to prepare a drive for APFS formatting.
/// </summary>
public class DiskPartService
{
    /// <summary>
    /// Runs a diskpart script and returns the combined output.
    /// </summary>
    /// <param name="scriptLines">Lines of the diskpart script to execute.</param>
    /// <returns>The combined stdout/stderr output from diskpart.</returns>
    public string RunScript(IEnumerable<string> scriptLines)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"apfsformat_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllLines(scriptPath, scriptLines);

            var psi = new ProcessStartInfo
            {
                FileName = "diskpart.exe",
                Arguments = $"/s \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start diskpart process.");

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());

            bool exited = process.WaitForExit(60_000); // 60 second timeout
            if (!exited)
            {
                process.Kill();
                throw new TimeoutException("diskpart did not complete within the 60-second timeout.");
            }

            return output.ToString();
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    /// <summary>
    /// Prepares a disk for APFS use by cleaning it, converting to GPT,
    /// and creating a single partition with the APFS partition type GUID.
    /// </summary>
    /// <param name="diskIndex">The diskpart disk index to format.</param>
    /// <param name="volumeLabel">Label to assign to the partition.</param>
    /// <returns>A <see cref="FormatResult"/> indicating success or failure.</returns>
    public FormatResult PrepareForApfs(int diskIndex, string volumeLabel = "APFS")
    {
        // Sanitize label: max 11 chars for FAT32 compatibility, no spaces for diskpart
        string safeLabel = SanitizeLabel(volumeLabel);

        // diskpart script to:
        // 1. Select the target disk
        // 2. Clean all existing partition data
        // 3. Convert to GPT (required for APFS)
        // 4. Create a primary partition spanning the full disk
        // 5. Set the partition type GUID to the APFS container GUID
        var script = new[]
        {
            $"select disk {diskIndex}",
            "clean",
            "convert gpt",
            "create partition primary",
            // APFS Container partition type GUID (Apple_APFS)
            "set id=7C3457EF-0000-11AA-AA11-00306543ECAC",
            "exit"
        };

        string output;
        try
        {
            output = RunScript(script);
        }
        catch (Exception ex)
        {
            return FormatResult.Fail($"diskpart failed to run: {ex.Message}");
        }

        // Check for common error indicators in diskpart output
        if (output.Contains("Virtual Disk Service error", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("There is no disk selected", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("is not recognized", StringComparison.OrdinalIgnoreCase))
        {
            return FormatResult.Fail(
                "diskpart reported an error during formatting. Ensure the disk is accessible and try again.",
                output);
        }

        // Verify key expected output phrases indicating success
        bool cleaned = output.Contains("DiskPart succeeded in cleaning the disk",
            StringComparison.OrdinalIgnoreCase);
        bool converted = output.Contains("DiskPart successfully converted the selected disk to GPT format",
            StringComparison.OrdinalIgnoreCase);
        bool partitionCreated = output.Contains("DiskPart succeeded in creating the specified partition",
            StringComparison.OrdinalIgnoreCase);

        if (!cleaned || !converted || !partitionCreated)
        {
            return FormatResult.Fail(
                "diskpart did not complete all required steps successfully. Check the output for details.",
                output);
        }

        return FormatResult.Ok(
            $"Disk {diskIndex} has been successfully prepared for APFS. " +
            "The partition has been created with the Apple APFS partition type GUID.",
            output);
    }

    private static string SanitizeLabel(string label)
    {
        // Remove characters that are not alphanumeric, hyphen, or underscore
        var safe = new StringBuilder();
        foreach (char c in label)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                safe.Append(c);
        }

        string result = safe.ToString();
        return result.Length > 0 ? result[..Math.Min(result.Length, 11)] : "APFS";
    }
}
