using System.Text;
using APFSFormatter.Models;

namespace APFSFormatter.Services;

public class HfsPlusVolumeBrowser : IRawVolumeBrowser
{
    private const ushort HfsPlusSignature = 0x482B;
    private const ushort HfsxSignature = 0x4858;
    private const uint RootFolderId = 2;
    private const sbyte LeafNodeKind = -1;
    private const sbyte HeaderNodeKind = 1;
    private const ushort FolderRecordType = 0x0001;
    private const ushort FileRecordType = 0x0002;
    private const int MaxEntriesPerSection = 10;
    private static readonly Encoding BigEndianUnicode = Encoding.BigEndianUnicode;

    public BrowseResult? TryBrowseRoot(string driveLetter)
    {
        string normalizedDriveLetter = NormalizeDriveLetter(driveLetter);
        string devicePath = $"\\\\.\\{normalizedDriveLetter}";

        try
        {
            using var stream = OpenRawVolume(devicePath);
            return TryBrowseRoot(stream, normalizedDriveLetter, normalizedDriveLetter + Path.DirectorySeparatorChar);
        }
        catch (UnauthorizedAccessException)
        {
            return BrowseResult.Fail(
                $"Windows assigned '{normalizedDriveLetter}', but raw HFS+ browsing requires Administrator privileges.");
        }
        catch (IOException ex)
        {
            return BrowseResult.Fail(
                $"Windows assigned '{normalizedDriveLetter}', but the raw volume could not be opened for HFS+ browsing. {ex.Message}");
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException || ex is InvalidDataException)
        {
            return BrowseResult.Fail(
                $"Windows assigned '{normalizedDriveLetter}', and the volume looks like HFS+, but its metadata layout could not be parsed safely. {ex.Message}");
        }
    }

    public FileCopyResult? TryCopyRootFile(string driveLetter, string fileName, string destinationDirectory)
    {
        string normalizedDriveLetter = NormalizeDriveLetter(driveLetter);
        string devicePath = $"\\\\.\\{normalizedDriveLetter}";

        try
        {
            using var stream = OpenRawVolume(devicePath);
            return TryCopyRootFile(stream, normalizedDriveLetter, fileName, destinationDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            return FileCopyResult.Fail(
                $"Raw HFS+ file copy from '{normalizedDriveLetter}' requires Administrator privileges.");
        }
        catch (IOException ex)
        {
            return FileCopyResult.Fail(
                $"The raw HFS+ volume '{normalizedDriveLetter}' could not be opened for file copy. {ex.Message}");
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException || ex is InvalidDataException)
        {
            return FileCopyResult.Fail(
                $"The HFS+ metadata on '{normalizedDriveLetter}' could not be parsed safely for file copy. {ex.Message}");
        }
    }

    public BrowseResult? TryBrowseRoot(Stream stream, string sourcePath) =>
        TryBrowseRoot(stream, ExtractDriveLetter(sourcePath), sourcePath);

    public BrowseResult? TryBrowseRoot(Stream stream, string driveLetter, string sourcePath)
    {
        VolumeContext? context = TryReadVolumeContext(stream);
        if (context is null)
            return null;

        var directories = new List<string>();
        var files = new List<HfsPlusRootFileRecord>();

        EnumerateRootRecords(
            stream,
            context,
            (name, recordType, fileRecord) =>
            {
                if (recordType == FolderRecordType)
                    directories.Add(name);
                else if (recordType == FileRecordType && fileRecord is not null)
                    files.Add(fileRecord);
            });

        if (directories.Count > MaxEntriesPerSection)
            directories = directories.Take(MaxEntriesPerSection).ToList();

        if (files.Count > MaxEntriesPerSection)
            files = files.Take(MaxEntriesPerSection).ToList();

        IReadOnlyList<BrowseFileEntry> fileEntries = files
            .Select(file => new BrowseFileEntry
            {
                Name = file.Name,
                CanCopy = file.IsCopyable,
                CopyUnavailableReason = file.CopyUnavailableReason
            })
            .ToArray();

        string message = directories.Count == 0 && files.Count == 0
            ? $"Successfully parsed the HFS+ root at '{sourcePath}', and the root directory is empty."
            : $"Successfully parsed the HFS+ root at '{sourcePath}'.";

        return BrowseResult.Ok(
            message,
            BrowseSourceKind.HfsPlusRaw,
            driveLetter,
            sourcePath,
            directories,
            fileEntries);
    }

    public FileCopyResult? TryCopyRootFile(Stream stream, string driveLetter, string fileName, string destinationDirectory)
    {
        VolumeContext? context = TryReadVolumeContext(stream);
        if (context is null)
            return null;

        HfsPlusRootFileRecord? target = null;
        EnumerateRootRecords(
            stream,
            context,
            (name, recordType, fileRecord) =>
            {
                if (recordType == FileRecordType &&
                    fileRecord is not null &&
                    string.Equals(name, fileName, StringComparison.Ordinal))
                {
                    target = fileRecord;
                }
            },
            stopWhen: () => target is not null);

        if (target is null)
        {
            return FileCopyResult.Fail(
                $"The file '{fileName}' was not found at the HFS+ root of '{driveLetter}'.");
        }

        if (!target.IsCopyable)
        {
            return FileCopyResult.Fail(
                target.CopyUnavailableReason ?? $"The file '{fileName}' cannot be copied by this HFS+ extractor.");
        }

        byte[] fileData = ReadFileData(stream, context, target);
        Directory.CreateDirectory(destinationDirectory);

        string destinationPath = Path.Combine(destinationDirectory, target.Name);
        File.WriteAllBytes(destinationPath, fileData);

        return FileCopyResult.Ok(
            $"Copied '{target.Name}' to '{destinationPath}'.",
            destinationPath);
    }

    private static FileStream OpenRawVolume(string devicePath) =>
        new(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: false);

    private static VolumeContext? TryReadVolumeContext(Stream stream)
    {
        VolumeHeaderInfo? volumeHeader = ReadVolumeHeader(stream);
        if (volumeHeader is null || volumeHeader.CatalogExtents.Count == 0)
            return null;

        CatalogHeaderInfo? catalogHeader = ReadCatalogHeader(stream, volumeHeader);
        if (catalogHeader is null)
            return null;

        return new VolumeContext(volumeHeader, catalogHeader);
    }

    private static void EnumerateRootRecords(
        Stream stream,
        VolumeContext context,
        Action<string, ushort, HfsPlusRootFileRecord?> visitor,
        Func<bool>? stopWhen = null)
    {
        uint currentNode = context.CatalogHeader.FirstLeafNode;
        int safetyCounter = 0;

        while (currentNode != 0 && safetyCounter < context.CatalogHeader.TotalNodes)
        {
            byte[] node = ReadCatalogNode(stream, context.VolumeHeader, context.CatalogHeader.NodeSize, currentNode);
            if (node.Length < context.CatalogHeader.NodeSize)
                break;

            EnumerateLeafNode(node, visitor, stopWhen);
            if (stopWhen?.Invoke() == true)
                break;

            currentNode = ReadUInt32BE(node, 0);
            safetyCounter++;
        }
    }

    private static void EnumerateLeafNode(
        byte[] node,
        Action<string, ushort, HfsPlusRootFileRecord?> visitor,
        Func<bool>? stopWhen)
    {
        if ((sbyte)node[8] != LeafNodeKind)
            return;

        ushort numRecords = ReadUInt16BE(node, 10);
        for (int recordIndex = 0; recordIndex < numRecords; recordIndex++)
        {
            if (stopWhen?.Invoke() == true)
                return;

            if (!TryReadLeafRecord(node, recordIndex, out ReadOnlySpan<byte> record))
                continue;

            if (!TryReadRecordKey(record, out uint parentId, out string name, out int dataOffset))
                continue;

            if (parentId != RootFolderId)
                continue;

            ushort recordType = ReadUInt16BE(record, dataOffset);
            HfsPlusRootFileRecord? fileRecord = recordType == FileRecordType
                ? ParseRootFileRecord(name, record, dataOffset)
                : null;

            visitor(name, recordType, fileRecord);
        }
    }

    private static bool TryReadLeafRecord(byte[] node, int recordIndex, out ReadOnlySpan<byte> record)
    {
        ushort numRecords = ReadUInt16BE(node, 10);
        ushort recordStart = ReadRecordOffset(node, recordIndex);
        ushort recordEnd = recordIndex == numRecords - 1
            ? ReadFreeSpaceOffset(node, numRecords)
            : ReadRecordOffset(node, recordIndex + 1);

        if (recordStart >= recordEnd || recordEnd > node.Length)
        {
            record = default;
            return false;
        }

        record = node.AsSpan(recordStart, recordEnd - recordStart);
        return true;
    }

    private static bool TryReadRecordKey(
        ReadOnlySpan<byte> record,
        out uint parentId,
        out string name,
        out int dataOffset)
    {
        parentId = 0;
        name = string.Empty;
        dataOffset = 0;

        if (record.Length < 10)
            return false;

        ushort keyLength = ReadUInt16BE(record, 0);
        dataOffset = 2 + keyLength;
        if (dataOffset + 2 > record.Length)
            return false;

        parentId = ReadUInt32BE(record, 2);
        ushort nameLength = ReadUInt16BE(record, 6);
        int nameByteLength = nameLength * 2;
        if (8 + nameByteLength > record.Length)
            return false;

        name = BigEndianUnicode.GetString(record.Slice(8, nameByteLength));
        return true;
    }

    private static HfsPlusRootFileRecord? ParseRootFileRecord(string name, ReadOnlySpan<byte> record, int dataOffset)
    {
        const int dataForkOffset = 88;
        const int forkRecordLength = 80;
        const int forkExtentsOffset = 16;

        if (dataOffset + dataForkOffset + forkRecordLength > record.Length)
            return null;

        int forkOffset = dataOffset + dataForkOffset;
        ulong logicalSize = ReadUInt64BE(record, forkOffset);
        uint totalBlocks = ReadUInt32BE(record, forkOffset + 12);

        var extents = new List<ExtentDescriptor>();
        uint inlineBlocks = 0;
        for (int i = 0; i < 8; i++)
        {
            int extentOffset = forkOffset + forkExtentsOffset + (i * 8);
            uint startBlock = ReadUInt32BE(record, extentOffset);
            uint blockCount = ReadUInt32BE(record, extentOffset + 4);
            if (blockCount == 0)
                continue;

            inlineBlocks += blockCount;
            extents.Add(new ExtentDescriptor(startBlock, blockCount));
        }

        string? copyUnavailableReason = totalBlocks > inlineBlocks
            ? $"The file '{name}' uses overflow extents, which are not supported by this extractor yet."
            : null;

        return new HfsPlusRootFileRecord(name, logicalSize, totalBlocks, extents, copyUnavailableReason);
    }

    private static byte[] ReadFileData(Stream stream, VolumeContext context, HfsPlusRootFileRecord fileRecord)
    {
        if (fileRecord.LogicalSize == 0)
            return Array.Empty<byte>();

        if (fileRecord.LogicalSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"The file '{fileRecord.Name}' is too large for this extractor.");
        }

        byte[] buffer = new byte[(int)fileRecord.LogicalSize];
        int bytesWritten = 0;
        ulong remainingBytes = fileRecord.LogicalSize;
        int blockSize = (int)context.VolumeHeader.BlockSize;

        foreach (ExtentDescriptor extent in fileRecord.Extents)
        {
            ulong extentBytes = (ulong)extent.BlockCount * context.VolumeHeader.BlockSize;
            int bytesToCopy = (int)Math.Min(extentBytes, remainingBytes);
            if (bytesToCopy == 0)
                break;

            int physicalReadLength = bytesToCopy;
            if (blockSize > 0 && physicalReadLength % blockSize != 0)
            {
                physicalReadLength = ((physicalReadLength / blockSize) + 1) * blockSize;
                physicalReadLength = (int)Math.Min((ulong)physicalReadLength, extentBytes);
            }

            ulong physicalOffset = (ulong)extent.StartBlock * context.VolumeHeader.BlockSize;
            stream.Seek((long)physicalOffset, SeekOrigin.Begin);

            if (physicalReadLength == bytesToCopy)
            {
                int directRead = stream.Read(buffer, bytesWritten, bytesToCopy);
                if (directRead != bytesToCopy)
                {
                    throw new InvalidDataException(
                        $"The HFS+ data fork for '{fileRecord.Name}' could not be read completely.");
                }
            }
            else
            {
                byte[] physicalBuffer = new byte[physicalReadLength];
                int read = stream.Read(physicalBuffer, 0, physicalReadLength);
                if (read < bytesToCopy)
                {
                    throw new InvalidDataException(
                        $"The HFS+ data fork for '{fileRecord.Name}' could not be read completely.");
                }

                Array.Copy(physicalBuffer, 0, buffer, bytesWritten, bytesToCopy);
            }

            bytesWritten += bytesToCopy;
            remainingBytes -= (ulong)bytesToCopy;
        }

        if (remainingBytes != 0)
        {
            throw new InvalidDataException(
                $"The HFS+ data fork for '{fileRecord.Name}' ended before the full logical size was read.");
        }

        return buffer;
    }

    private static VolumeHeaderInfo? ReadVolumeHeader(Stream stream)
    {
        byte[] header = new byte[512];
        stream.Seek(1024, SeekOrigin.Begin);
        if (stream.Read(header, 0, header.Length) != header.Length)
            return null;

        ushort signature = ReadUInt16BE(header, 0);
        if (signature != HfsPlusSignature && signature != HfsxSignature)
            return null;

        uint blockSize = ReadUInt32BE(header, 40);
        if (blockSize == 0)
            return null;

        ulong logicalSize = ReadUInt64BE(header, 0x110);
        uint totalBlocks = ReadUInt32BE(header, 0x110 + 12);

        var extents = new List<ExtentDescriptor>();
        int extentsOffset = 0x110 + 16;
        for (int i = 0; i < 8; i++)
        {
            uint startBlock = ReadUInt32BE(header, extentsOffset + (i * 8));
            uint blockCount = ReadUInt32BE(header, extentsOffset + (i * 8) + 4);
            if (blockCount == 0)
                continue;

            extents.Add(new ExtentDescriptor(startBlock, blockCount));
        }

        return new VolumeHeaderInfo(blockSize, logicalSize, totalBlocks, extents);
    }

    private static CatalogHeaderInfo? ReadCatalogHeader(Stream stream, VolumeHeaderInfo volumeHeader)
    {
        byte[] headerNode = ReadForkBytes(stream, volumeHeader, 0, 4096);
        if (headerNode.Length < 256)
            return null;

        if ((sbyte)headerNode[8] != HeaderNodeKind)
            return null;

        const int headerRecordStart = 14;
        if (headerRecordStart + 26 > headerNode.Length)
            return null;

        uint firstLeafNode = ReadUInt32BE(headerNode, headerRecordStart + 10);
        ushort nodeSize = ReadUInt16BE(headerNode, headerRecordStart + 18);
        uint totalNodes = ReadUInt32BE(headerNode, headerRecordStart + 22);
        if (nodeSize == 0 || totalNodes == 0)
            return null;

        return new CatalogHeaderInfo(firstLeafNode, nodeSize, totalNodes);
    }

    private static byte[] ReadCatalogNode(Stream stream, VolumeHeaderInfo volumeHeader, ushort nodeSize, uint nodeNumber)
    {
        ulong offset = (ulong)nodeNumber * nodeSize;
        return ReadForkBytes(stream, volumeHeader, offset, nodeSize);
    }

    private static byte[] ReadForkBytes(Stream stream, VolumeHeaderInfo volumeHeader, ulong forkOffset, int byteCount)
    {
        byte[] buffer = new byte[byteCount];
        int bytesRead = 0;
        ulong remainingOffset = forkOffset;
        int remainingBytes = byteCount;

        foreach (ExtentDescriptor extent in volumeHeader.CatalogExtents)
        {
            ulong extentBytes = (ulong)extent.BlockCount * volumeHeader.BlockSize;
            if (remainingOffset >= extentBytes)
            {
                remainingOffset -= extentBytes;
                continue;
            }

            ulong physicalOffset = ((ulong)extent.StartBlock * volumeHeader.BlockSize) + remainingOffset;
            int availableBytes = (int)Math.Min((ulong)remainingBytes, extentBytes - remainingOffset);

            stream.Seek((long)physicalOffset, SeekOrigin.Begin);
            int read = stream.Read(buffer, bytesRead, availableBytes);
            bytesRead += read;
            remainingBytes -= read;
            remainingOffset = 0;

            if (remainingBytes == 0 || read < availableBytes)
                break;
        }

        if (bytesRead == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, bytesRead);
        return buffer;
    }

    private static ushort ReadRecordOffset(byte[] node, int recordIndex)
    {
        int offsetPosition = node.Length - 2 - (recordIndex * 2);
        return ReadUInt16BE(node, offsetPosition);
    }

    private static ushort ReadFreeSpaceOffset(byte[] node, ushort numRecords)
    {
        int offsetPosition = node.Length - 2 - (numRecords * 2);
        return ReadUInt16BE(node, offsetPosition);
    }

    private static string NormalizeDriveLetter(string driveLetter)
    {
        string normalized = driveLetter.Trim();
        if (normalized.EndsWith(Path.DirectorySeparatorChar) || normalized.EndsWith(Path.AltDirectorySeparatorChar))
            normalized = normalized[..^1];

        if (!normalized.EndsWith(":", StringComparison.Ordinal))
            normalized += ":";

        return normalized;
    }

    private static string ExtractDriveLetter(string sourcePath)
    {
        string trimmed = sourcePath.Trim();
        return trimmed.Length >= 2 && trimmed[1] == ':' ? trimmed[..2] : trimmed;
    }

    private static ushort ReadUInt16BE(byte[] buffer, int offset) =>
        ReadUInt16BE(buffer.AsSpan(offset, 2), 0);

    private static ushort ReadUInt16BE(ReadOnlySpan<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static uint ReadUInt32BE(byte[] buffer, int offset) =>
        ReadUInt32BE(buffer.AsSpan(offset, 4), 0);

    private static uint ReadUInt32BE(ReadOnlySpan<byte> buffer, int offset) =>
        ((uint)buffer[offset] << 24) |
        ((uint)buffer[offset + 1] << 16) |
        ((uint)buffer[offset + 2] << 8) |
        buffer[offset + 3];

    private static ulong ReadUInt64BE(byte[] buffer, int offset) =>
        ((ulong)ReadUInt32BE(buffer, offset) << 32) | ReadUInt32BE(buffer, offset + 4);

    private static ulong ReadUInt64BE(ReadOnlySpan<byte> buffer, int offset) =>
        ((ulong)ReadUInt32BE(buffer, offset) << 32) | ReadUInt32BE(buffer, offset + 4);

    private sealed record VolumeHeaderInfo(uint BlockSize, ulong CatalogLogicalSize, uint CatalogTotalBlocks, List<ExtentDescriptor> CatalogExtents);
    private sealed record CatalogHeaderInfo(uint FirstLeafNode, ushort NodeSize, uint TotalNodes);
    private sealed record ExtentDescriptor(uint StartBlock, uint BlockCount);
    private sealed record VolumeContext(VolumeHeaderInfo VolumeHeader, CatalogHeaderInfo CatalogHeader);
    private sealed record HfsPlusRootFileRecord(string Name, ulong LogicalSize, uint TotalBlocks, List<ExtentDescriptor> Extents, string? CopyUnavailableReason)
    {
        public bool IsCopyable => string.IsNullOrEmpty(CopyUnavailableReason);
    }
}