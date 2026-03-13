namespace APFSFormatter.Services;

/// <summary>
/// Resolves a Windows drive letter for a physical disk index.
/// </summary>
public interface IDriveLetterResolver
{
    string GetDriveLetterByDiskIndex(int diskIndex);
}