using System.Runtime.InteropServices;
using System.Text;
using APFSFormatter.Models;

namespace APFSFormatter.Services;

/// <summary>
/// Writes a minimal APFS container superblock to a raw physical disk device,
/// making the drive recognizable as an APFS-formatted volume by macOS.
/// </summary>
public class ApfsContainerWriter
{
    // APFS magic constants
    private const uint NX_MAGIC = 0x4253584E;          // 'NXSB' little-endian
    private const uint OBJECT_TYPE_NX_SUPERBLOCK = 0x80000001; // physical NX superblock
    private const uint APFS_BLOCK_SIZE = 4096;
    private const ulong NX_FEATURES_DEFAULT = 0;
    private const ulong NX_READONLY_COMPAT_FEATURES = 0;
    private const ulong NX_INCOMPAT_FEATURES = 0;
    private const uint NX_MAX_FILE_SYSTEMS = 100;

    // Checkpoint descriptor/data area sizes (in blocks)
    private const uint XP_DESC_BLOCKS = 8;
    private const uint XP_DATA_BLOCKS = 8;

    /// <summary>
    /// Writes an APFS container superblock (Block 0) to the specified physical disk.
    /// Requires administrator privileges.
    /// </summary>
    /// <param name="diskIndex">Windows disk index (e.g., 1 for \\.\PhysicalDrive1).</param>
    /// <param name="totalDiskBytes">Total size of the disk in bytes.</param>
    /// <returns>A <see cref="FormatResult"/> indicating success or failure.</returns>
    public FormatResult WriteContainerSuperblock(int diskIndex, ulong totalDiskBytes)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FormatResult.Fail("APFS superblock writing is only supported on Windows.");

        string devicePath = $"\\\\.\\PhysicalDrive{diskIndex}";
        ulong blockCount = totalDiskBytes / APFS_BLOCK_SIZE;

        if (blockCount < 16)
            return FormatResult.Fail("The disk is too small to hold an APFS container.");

        byte[] superblock = BuildSuperblock(blockCount);

        try
        {
            return WriteSuperblockToDevice(devicePath, superblock);
        }
        catch (UnauthorizedAccessException)
        {
            return FormatResult.Fail(
                "Access denied. Please run the application as Administrator.");
        }
        catch (Exception ex)
        {
            return FormatResult.Fail($"Failed to write APFS superblock: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a 4096-byte APFS container superblock (nx_superblock_t) with
    /// a valid Fletcher64 checksum.
    /// </summary>
    private static byte[] BuildSuperblock(ulong blockCount)
    {
        byte[] block = new byte[APFS_BLOCK_SIZE];
        using var writer = new BinaryWriter(new MemoryStream(block));

        // --- obj_phys_t header (32 bytes at offset 0x00) ---
        writer.Write(0UL);                      // o_cksum (8 bytes) - filled in later
        writer.Write(1UL);                      // o_oid = 1 (container superblock OID)
        writer.Write(1UL);                      // o_xid = 1 (initial transaction ID)
        writer.Write(OBJECT_TYPE_NX_SUPERBLOCK);// o_type (4 bytes)
        writer.Write(0u);                       // o_subtype (4 bytes)

        // --- nx_superblock_t fields ---
        writer.Write(NX_MAGIC);                 // nx_magic (0x20)
        writer.Write(APFS_BLOCK_SIZE);          // nx_block_size (0x24)
        writer.Write(blockCount);               // nx_block_count (0x28)
        writer.Write(NX_FEATURES_DEFAULT);      // nx_features (0x30)
        writer.Write(NX_READONLY_COMPAT_FEATURES); // nx_readonly_compatible_features (0x38)
        writer.Write(NX_INCOMPAT_FEATURES);     // nx_incompatible_features (0x40)

        // nx_uuid: generate a random UUID (0x48, 16 bytes)
        byte[] uuid = Guid.NewGuid().ToByteArray();
        writer.Write(uuid);

        writer.Write(1024UL);                   // nx_next_oid (0x58): next available OID
        writer.Write(2UL);                      // nx_next_xid (0x60): next transaction ID

        // Checkpoint descriptor area (0x68)
        writer.Write(XP_DESC_BLOCKS);           // nx_xp_desc_blocks
        writer.Write(XP_DATA_BLOCKS);           // nx_xp_data_blocks
        writer.Write(1L);                       // nx_xp_desc_base (block 1, after superblock)
        writer.Write((long)(1 + XP_DESC_BLOCKS)); // nx_xp_data_base
        writer.Write(0u);                       // nx_xp_desc_next
        writer.Write(0u);                       // nx_xp_data_next
        writer.Write(0u);                       // nx_xp_desc_index
        writer.Write(1u);                       // nx_xp_desc_len
        writer.Write(0u);                       // nx_xp_data_index
        writer.Write(1u);                       // nx_xp_data_len

        // Special object OIDs (0x98)
        writer.Write(0UL);                      // nx_spaceman_oid
        writer.Write(0UL);                      // nx_omap_oid
        writer.Write(0UL);                      // nx_reaper_oid

        // nx_test_type + nx_max_file_systems (0xB0)
        writer.Write(0u);                       // nx_test_type
        writer.Write(NX_MAX_FILE_SYSTEMS);      // nx_max_file_systems

        // Remaining bytes in block are already zero-initialized.

        // Compute and write Fletcher64 checksum into bytes 0-7
        ulong checksum = ComputeApfsFletcher64(block);
        BitConverter.GetBytes(checksum).CopyTo(block, 0);

        return block;
    }

    /// <summary>
    /// Computes the APFS variant of the Fletcher64 checksum over a block.
    /// The first 8 bytes (checksum field) are treated as zero during computation.
    /// The result is stored as two 32-bit values such that re-computing the checksum
    /// over the entire block (including the stored checksum) yields zero.
    /// </summary>
    private static ulong ComputeApfsFletcher64(byte[] block)
    {
        const uint modValue = 0xFFFFFFFF; // 2^32 - 1

        ulong sum1 = 0;
        ulong sum2 = 0;

        // Process the block as 32-bit little-endian words, treating bytes 0-7 as zero
        for (int i = 0; i < block.Length; i += 4)
        {
            uint word = (i < 8) ? 0u : BitConverter.ToUInt32(block, i);
            sum1 = (sum1 + word) % modValue;
            sum2 = (sum2 + sum1) % modValue;
        }

        ulong check1 = modValue - ((sum1 + sum2) % modValue);
        ulong check2 = modValue - ((sum1 + check1) % modValue);

        return (check2 << 32) | check1;
    }

    /// <summary>
    /// Opens the physical drive and writes the superblock to block 0.
    /// Uses Windows native file I/O to access the raw device.
    /// </summary>
    private static FormatResult WriteSuperblockToDevice(string devicePath, byte[] superblock)
    {
        FileStream stream;
        try
        {
            // Open the physical drive with shared read/write access.
            // FileShare.ReadWrite is required on Windows because the kernel itself
            // holds the disk open; FileShare.None will fail on mounted drives.
            stream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: false);
        }
        catch (IOException ex)
        {
            return FormatResult.Fail(
                $"Could not open '{devicePath}' for writing. " +
                $"Ensure no other processes are accessing the drive and try again. ({ex.Message})");
        }

        using (stream)
        {
            // Seek to the beginning (block 0) and write the superblock
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(superblock, 0, superblock.Length);
            stream.Flush();
        }

        return FormatResult.Ok(
            $"APFS container superblock written successfully to {devicePath}.");
    }
}
