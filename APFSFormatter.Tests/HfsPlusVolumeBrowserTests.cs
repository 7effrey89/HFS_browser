using System.Text;
using APFSFormatter.Models;
using APFSFormatter.Services;
using Xunit;

namespace APFSFormatter.Tests;

public class HfsPlusVolumeBrowserTests
{
    [Fact]
    public void TryBrowseRoot_ParsesSyntheticHfsPlusRoot()
    {
        byte[] image = BuildSyntheticHfsPlusImage();
        using var stream = new MemoryStream(image, writable: false);

        var browser = new HfsPlusVolumeBrowser();
        var result = browser.TryBrowseRoot(stream, "R:\\");

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal(["Apps"], result.Directories);
        Assert.Equal(["ReadMe.txt"], result.Files);
        Assert.Equal(BrowseSourceKind.HfsPlusRaw, result.SourceKind);
        Assert.True(result.FileEntries[0].CanCopy);
    }

    [Fact]
    public void TryBrowseRoot_ReturnsNull_ForNonHfsPlusSignature()
    {
        byte[] image = new byte[16_384];
        using var stream = new MemoryStream(image, writable: false);

        var browser = new HfsPlusVolumeBrowser();
        var result = browser.TryBrowseRoot(stream, "R:\\");

        Assert.Null(result);
    }

    [Fact]
    public void TryCopyRootFile_CopiesSyntheticRootFile()
    {
        byte[] image = BuildSyntheticHfsPlusImage();
        using var stream = new MemoryStream(image, writable: false);

        string destinationDirectory = Path.Combine(Path.GetTempPath(), "apfsformatter-hfs-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var browser = new HfsPlusVolumeBrowser();
            FileCopyResult? result = browser.TryCopyRootFile(stream, "R:", "ReadMe.txt", destinationDirectory);

            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.NotNull(result.DestinationPath);
            Assert.True(File.Exists(result.DestinationPath));
            Assert.Equal("hello from hfs+", File.ReadAllText(result.DestinationPath));
        }
        finally
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
        }
    }

    private static byte[] BuildSyntheticHfsPlusImage()
    {
        const int blockSize = 4096;
        byte[] image = new byte[blockSize * 6];

        WriteUInt16BE(image, 1024, 0x482B);
        WriteUInt16BE(image, 1026, 4);
        WriteUInt32BE(image, 1024 + 40, blockSize);

        int catalogForkOffset = 1024 + 0x110;
        WriteUInt64BE(image, catalogForkOffset, 8192);
        WriteUInt32BE(image, catalogForkOffset + 8, 0);
        WriteUInt32BE(image, catalogForkOffset + 12, 2);
        WriteUInt32BE(image, catalogForkOffset + 16, 2);
        WriteUInt32BE(image, catalogForkOffset + 20, 2);

        int catalogFileOffset = blockSize * 2;
        WriteCatalogHeaderNode(image, catalogFileOffset, blockSize);
        WriteCatalogLeafNode(image, catalogFileOffset + blockSize, blockSize);
        WriteFileData(image, blockSize * 4, "hello from hfs+");

        return image;
    }

    private static void WriteCatalogHeaderNode(byte[] image, int offset, int nodeSize)
    {
        WriteUInt32BE(image, offset, 0);
        WriteUInt32BE(image, offset + 4, 1);
        image[offset + 8] = 0x01;
        image[offset + 9] = 0x00;
        WriteUInt16BE(image, offset + 10, 1);
        WriteUInt16BE(image, offset + 12, 0);

        int recordOffset = 14;
        WriteUInt16BE(image, offset + recordOffset + 0, 1);
        WriteUInt32BE(image, offset + recordOffset + 2, 1);
        WriteUInt32BE(image, offset + recordOffset + 6, 2);
        WriteUInt32BE(image, offset + recordOffset + 10, 1);
        WriteUInt32BE(image, offset + recordOffset + 14, 1);
        WriteUInt16BE(image, offset + recordOffset + 18, (ushort)nodeSize);
        WriteUInt16BE(image, offset + recordOffset + 20, 255);
        WriteUInt32BE(image, offset + recordOffset + 22, 2);
        WriteUInt32BE(image, offset + recordOffset + 26, 0);

        int freeSpaceOffset = recordOffset + 106;
        WriteUInt16BE(image, offset + nodeSize - 2, (ushort)recordOffset);
        WriteUInt16BE(image, offset + nodeSize - 4, (ushort)freeSpaceOffset);
    }

    private static void WriteCatalogLeafNode(byte[] image, int offset, int nodeSize)
    {
        WriteUInt32BE(image, offset, 0);
        WriteUInt32BE(image, offset + 4, 0);
        image[offset + 8] = 0xFF;
        image[offset + 9] = 0x01;
        WriteUInt16BE(image, offset + 10, 2);
        WriteUInt16BE(image, offset + 12, 0);

        int record0Offset = 14;
        int record1Offset = WriteCatalogRecord(image, offset + record0Offset, "Apps", isFolder: true);
        int record2Offset = record0Offset + record1Offset;
        int record2Length = WriteCatalogRecord(image, offset + record2Offset, "ReadMe.txt", isFolder: false);
        int freeSpaceOffset = record2Offset + record2Length;

        WriteUInt16BE(image, offset + nodeSize - 2, (ushort)record0Offset);
        WriteUInt16BE(image, offset + nodeSize - 4, (ushort)record2Offset);
        WriteUInt16BE(image, offset + nodeSize - 6, (ushort)freeSpaceOffset);
    }

    private static int WriteCatalogRecord(byte[] image, int offset, string name, bool isFolder)
    {
        byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(name);
        ushort keyLength = (ushort)(4 + 2 + nameBytes.Length);

        WriteUInt16BE(image, offset, keyLength);
        WriteUInt32BE(image, offset + 2, 2);
        WriteUInt16BE(image, offset + 6, (ushort)name.Length);
        Buffer.BlockCopy(nameBytes, 0, image, offset + 8, nameBytes.Length);
        int recordDataOffset = offset + 2 + keyLength;
        WriteUInt16BE(image, recordDataOffset, isFolder ? (ushort)1 : (ushort)2);

        if (isFolder)
            return 2 + keyLength + 2;

        int dataForkOffset = recordDataOffset + 88;
        WriteUInt64BE(image, dataForkOffset, 15);
        WriteUInt32BE(image, dataForkOffset + 8, 0);
        WriteUInt32BE(image, dataForkOffset + 12, 1);
        WriteUInt32BE(image, dataForkOffset + 16, 4);
        WriteUInt32BE(image, dataForkOffset + 20, 1);

        return 2 + keyLength + 168;
    }

    private static void WriteFileData(byte[] image, int offset, string content)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        Buffer.BlockCopy(bytes, 0, image, offset, bytes.Length);
    }

    private static void WriteUInt16BE(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteUInt64BE(byte[] buffer, int offset, ulong value)
    {
        WriteUInt32BE(buffer, offset, (uint)(value >> 32));
        WriteUInt32BE(buffer, offset + 4, (uint)value);
    }
}