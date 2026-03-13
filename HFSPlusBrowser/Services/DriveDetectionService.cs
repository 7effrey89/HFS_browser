using System.Management;
using HFSPlusBrowser.Models;

namespace HFSPlusBrowser.Services;

/// <summary>
/// Detects removable USB drives connected to the system using WMI.
/// </summary>
public class DriveDetectionService
    : IDriveLetterResolver
{
    /// <summary>
    /// Returns a list of removable USB drives currently connected to the system.
    /// </summary>
    public List<UsbDriveInfo> GetRemovableDrives()
    {
        var drives = new List<UsbDriveInfo>();

        try
        {
            // Query WMI for disk drives that are removable (USB drives)
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_DiskDrive WHERE MediaType = 'Removable Media' OR InterfaceType = 'USB'");

            foreach (ManagementObject disk in diskSearcher.Get())
            {
                string deviceId = disk["DeviceID"]?.ToString() ?? string.Empty;
                string model = disk["Model"]?.ToString()?.Trim() ?? "Unknown Drive";
                ulong size = disk["Size"] != null ? (ulong)disk["Size"] : 0UL;
                int diskIndex = disk["Index"] != null ? Convert.ToInt32(disk["Index"]) : -1;

                // Find the drive letter associated with this physical disk
                string driveLetter = GetDriveLetterForDisk(deviceId, diskIndex);

                drives.Add(new UsbDriveInfo
                {
                    DiskIndex = diskIndex,
                    DriveLetter = driveLetter,
                    Model = model,
                    SizeBytes = size,
                    DeviceId = deviceId
                });
            }
        }
        catch (ManagementException ex)
        {
            throw new InvalidOperationException(
                $"Failed to enumerate drives via WMI: {ex.Message}", ex);
        }

        return drives.OrderBy(d => d.DiskIndex).ToList();
    }

    /// <summary>
    /// Looks up the current drive letter for a removable disk by its disk index.
    /// Returns an empty string when Windows does not expose a logical drive letter.
    /// </summary>
    public string GetDriveLetterByDiskIndex(int diskIndex)
    {
        UsbDriveInfo? drive = GetRemovableDrives().FirstOrDefault(d => d.DiskIndex == diskIndex);
        return drive?.DriveLetter ?? string.Empty;
    }

    /// <summary>
    /// Resolves the Windows drive letter (e.g., "E:") for a given physical disk device ID.
    /// </summary>
    private static string GetDriveLetterForDisk(string deviceId, int diskIndex)
    {
        try
        {
            // Associate physical disk -> disk partition -> logical disk
            using var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{EscapeWmiString(deviceId)}'}} " +
                "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

            foreach (ManagementObject partition in partitionSearcher.Get())
            {
                using var logicalSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{EscapeWmiString(partition["DeviceID"]?.ToString() ?? string.Empty)}'}} " +
                    "WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject logical in logicalSearcher.Get())
                {
                    string? letter = logical["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(letter))
                        return letter;
                }
            }
        }
        catch (ManagementException)
        {
            // Drive letter lookup is best-effort; not critical
        }

        string storageDriveLetter = GetDriveLetterFromStoragePartitionProvider(diskIndex);
        if (!string.IsNullOrEmpty(storageDriveLetter))
            return storageDriveLetter;

        return string.Empty;
    }

    private static string GetDriveLetterFromStoragePartitionProvider(int diskIndex)
    {
        if (diskIndex < 0)
            return string.Empty;

        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    $"SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DiskNumber = {diskIndex}"));

            foreach (ManagementObject partition in searcher.Get())
            {
                string? driveLetter = partition["DriveLetter"]?.ToString();
                if (string.IsNullOrWhiteSpace(driveLetter))
                    continue;

                return driveLetter.EndsWith(":", StringComparison.Ordinal)
                    ? driveLetter
                    : driveLetter + ":";
            }
        }
        catch (ManagementException)
        {
            // Storage provider lookup is best-effort; not critical
        }
        catch (UnauthorizedAccessException)
        {
            // Some environments may restrict access to the storage provider namespace.
        }

        return string.Empty;
    }

    /// <summary>
    /// Escapes backslashes in a string for use in WMI queries.
    /// </summary>
    private static string EscapeWmiString(string value) =>
        value.Replace("\\", "\\\\");
}
