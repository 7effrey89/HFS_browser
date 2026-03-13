namespace APFSFormatter.Models;

/// <summary>
/// Result of a formatting operation.
/// </summary>
public class FormatResult
{
    /// <summary>Indicates whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable message describing the outcome.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Raw output from diskpart (for diagnostics).</summary>
    public string? DiskPartOutput { get; init; }

    public static FormatResult Ok(string message, string? diskPartOutput = null) =>
        new() { Success = true, Message = message, DiskPartOutput = diskPartOutput };

    public static FormatResult Fail(string message, string? diskPartOutput = null) =>
        new() { Success = false, Message = message, DiskPartOutput = diskPartOutput };
}
