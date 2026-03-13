namespace HFSPlusBrowser.Models;

public class FileCopyResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? DestinationPath { get; init; }

    public static FileCopyResult Ok(string message, string destinationPath) =>
        new() { Success = true, Message = message, DestinationPath = destinationPath };

    public static FileCopyResult Fail(string message) =>
        new() { Success = false, Message = message };
}