namespace HFSPlusBrowser.Models;

/// <summary>
/// Represents a removable USB drive detected on the system.
/// </summary>
public class UsbDriveInfo
{
    /// <summary>Disk index used by diskpart (e.g., 0, 1, 2).</summary>
    public int DiskIndex { get; init; }

    /// <summary>Drive letter assigned by Windows (e.g., "E:").</summary>
    public string DriveLetter { get; init; } = string.Empty;

    /// <summary>Friendly model/name of the drive.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Total size of the drive in bytes.</summary>
    public ulong SizeBytes { get; init; }

    /// <summary>Friendly formatted size string (e.g., "14.9 GB").</summary>
    public string SizeFormatted => FormatSize(SizeBytes);

    /// <summary>Device ID from WMI (e.g., "\\\\.\\PHYSICALDRIVE1").</summary>
    public string DeviceId { get; init; } = string.Empty;

    private static string FormatSize(ulong bytes)
    {
        if (bytes >= 1_099_511_627_776UL)
            return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824UL)
            return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576UL)
            return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }

    public override string ToString() =>
        $"Disk {DiskIndex}: {Model} ({SizeFormatted}){(string.IsNullOrEmpty(DriveLetter) ? "" : $" [{DriveLetter}]")}";
}
