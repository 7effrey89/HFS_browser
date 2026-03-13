using System.Diagnostics;
using System.Text;
using APFSFormatter.Models;

namespace APFSFormatter.Services;

/// <summary>
/// Executes diskpart commands to prepare a drive for APFS formatting.
/// </summary>
public class DiskPartService
{
    private const string ApfsPartitionTypeGuid = "7C3457EF-0000-11AA-AA11-00306543ECAC";

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

        var output = new StringBuilder();

        try
        {
            string cleanOutput = RunScript(new[]
            {
                $"select disk {diskIndex}",
                "clean",
                "exit"
            });

            AppendSection(output, "Clean output", cleanOutput);

            bool cleaned = cleanOutput.Contains(
                "DiskPart succeeded in cleaning the disk",
                StringComparison.OrdinalIgnoreCase);

            if (!cleaned)
            {
                return FormatResult.Fail(
                    "diskpart did not successfully clean the disk.",
                    output.ToString());
            }

            string convertOutput = RunScript(new[]
            {
                $"select disk {diskIndex}",
                "convert gpt",
                "exit"
            });

            AppendSection(output, "Convert output", convertOutput);

            bool converted = convertOutput.Contains(
                "DiskPart successfully converted the selected disk to GPT format",
                StringComparison.OrdinalIgnoreCase);
            bool alreadyGpt = convertOutput.Contains(
                "is not MBR formatted",
                StringComparison.OrdinalIgnoreCase);

            if (!converted && !alreadyGpt)
            {
                return FormatResult.Fail(
                    "diskpart could not convert the disk to GPT.",
                    output.ToString());
            }

            string partitionOutput = RunScript(new[]
            {
                $"select disk {diskIndex}",
                "create partition primary",
                $"set id={ApfsPartitionTypeGuid}",
                "exit"
            });

            AppendSection(output, "Partition output", partitionOutput);

            bool partitionCreated = partitionOutput.Contains(
                "DiskPart succeeded in creating the specified partition",
                StringComparison.OrdinalIgnoreCase);

            if (!partitionCreated)
            {
                return FormatResult.Fail(
                    "diskpart did not create the APFS partition successfully.",
                    output.ToString());
            }
        }
        catch (Exception ex)
        {
            return FormatResult.Fail($"diskpart failed to run: {ex.Message}");
        }

        // Check for common error indicators in diskpart output
        string combinedOutput = output.ToString();
        if (combinedOutput.Contains("Virtual Disk Service error", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("There is no disk selected", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("is not recognized", StringComparison.OrdinalIgnoreCase))
        {
            return FormatResult.Fail(
                "diskpart reported an error during formatting. Ensure the disk is accessible and try again.",
                combinedOutput);
        }

        return FormatResult.Ok(
            $"Disk {diskIndex} has been successfully prepared for APFS. " +
            "The partition has been created with the Apple APFS partition type GUID.",
            combinedOutput);
    }

    private static void AppendSection(StringBuilder builder, string title, string sectionOutput)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine($"{title}:");
        builder.AppendLine(sectionOutput.TrimEnd());
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
