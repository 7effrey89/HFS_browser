using APFSFormatter.Models;
using APFSFormatter.Services;
using Xunit;

namespace APFSFormatter.Tests;

public class DriveBrowseServiceTests
{
    [Fact]
    public void BrowseRoot_ReturnsFailure_WhenDriveLetterIsUnavailable()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver(string.Empty),
            new FakeFileSystemBrowser(),
            new FakeRawVolumeBrowser(),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        var result = service.BrowseRoot(3);

        Assert.False(result.Success);
        Assert.Contains("did not expose a drive letter", result.Message);
    }

    [Fact]
    public void BrowseRoot_ReturnsEntries_WhenRootCanBeEnumerated()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new FakeFileSystemBrowser(
                directories: ["Docs", "Media"],
                files: ["readme.txt"]),
            new FakeRawVolumeBrowser(),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        var result = service.BrowseRoot(1);

        Assert.True(result.Success);
        Assert.Equal("R:\\", result.RootPath);
        Assert.Equal(["Docs", "Media"], result.Directories);
        Assert.Equal(["readme.txt"], result.Files);
    }

    [Fact]
    public void BrowseRoot_ReturnsEmptySuccess_WhenRootHasNoEntries()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new FakeFileSystemBrowser(),
            new FakeRawVolumeBrowser(),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        var result = service.BrowseRoot(1);

        Assert.True(result.Success);
        Assert.Empty(result.Directories);
        Assert.Empty(result.Files);
        Assert.Contains("root directory is empty", result.Message);
    }

    [Fact]
    public void BrowseRoot_ReturnsFailure_WhenFileSystemEnumerationThrows()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new ThrowingFileSystemBrowser(new IOException("Mount failed")),
            new FakeRawVolumeBrowser(),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        var result = service.BrowseRoot(1);

        Assert.False(result.Success);
        Assert.Contains("could not be browsed", result.Message);
        Assert.Contains("Mount failed", result.Message);
    }

    [Fact]
    public void BrowseRoot_UsesRawVolumeBrowser_WhenWindowsEnumerationFails()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new ThrowingFileSystemBrowser(new IOException("Unrecognized file system")),
            new FakeRawVolumeBrowser(
                BrowseResult.Ok(
                    "Successfully parsed the HFS+ root at 'R:\\'.",
                    BrowseSourceKind.HfsPlusRaw,
                    "R:",
                    "R:\\",
                    ["Applications"],
                    [new BrowseFileEntry { Name = "ReadMe", CanCopy = true }])),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        var result = service.BrowseRoot(1);

        Assert.True(result.Success);
        Assert.Equal(["Applications"], result.Directories);
        Assert.Equal(["ReadMe"], result.Files);
    }

    [Fact]
    public void CopyFileToTemp_UsesRawVolumeBrowser_WhenBrowseSourceIsHfsPlus()
    {
        var rawBrowser = new FakeRawVolumeBrowser(
            BrowseResult.Ok(
                "Parsed HFS+ root.",
                BrowseSourceKind.HfsPlusRaw,
                "R:",
                "R:\\",
                [],
                [new BrowseFileEntry { Name = "ReadMe.txt", CanCopy = true }]),
            FileCopyResult.Ok("Copied file.", @"C:\Temp\ReadMe.txt"));

        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new ThrowingFileSystemBrowser(new IOException("Unrecognized file system")),
            rawBrowser,
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        BrowseResult browseResult = service.BrowseRoot(1);
        FileCopyResult copyResult = service.CopyFileToTemp(browseResult, 0);

        Assert.True(copyResult.Success);
        Assert.Equal("ReadMe.txt", rawBrowser.LastCopiedFileName);
        Assert.Equal(@"C:\Temp", rawBrowser.LastCopyDestinationDirectory);
    }

    [Fact]
    public void CopyFileToTemp_Fails_WhenSelectedFileIsMarkedUnavailable()
    {
        var service = new DriveBrowseService(
            new FakeDriveLetterResolver("R:"),
            new ThrowingFileSystemBrowser(new IOException("Unrecognized file system")),
            new FakeRawVolumeBrowser(
                BrowseResult.Ok(
                    "Parsed HFS+ root.",
                    BrowseSourceKind.HfsPlusRaw,
                    "R:",
                    "R:\\",
                    [],
                    [new BrowseFileEntry
                    {
                        Name = "Huge.mov",
                        CanCopy = false,
                        CopyUnavailableReason = "Uses overflow extents."
                    }])),
            maxRetryAttempts: 1,
            retryDelayMilliseconds: 0);

        BrowseResult browseResult = service.BrowseRoot(1);
        FileCopyResult copyResult = service.CopyFileToTemp(browseResult, 0);

        Assert.False(copyResult.Success);
        Assert.Contains("overflow extents", copyResult.Message);
    }

    private sealed class FakeDriveLetterResolver : IDriveLetterResolver
    {
        private readonly string _driveLetter;

        public FakeDriveLetterResolver(string driveLetter)
        {
            _driveLetter = driveLetter;
        }

        public string GetDriveLetterByDiskIndex(int diskIndex) => _driveLetter;
    }

    private sealed class FakeFileSystemBrowser : IFileSystemBrowser
    {
        private readonly IReadOnlyList<string> _directories;
        private readonly IReadOnlyList<string> _files;

        public FakeFileSystemBrowser(
            IReadOnlyList<string>? directories = null,
            IReadOnlyList<string>? files = null)
        {
            _directories = directories ?? Array.Empty<string>();
            _files = files ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> EnumerateDirectories(string rootPath, int maxEntries) => _directories;

        public IReadOnlyList<string> EnumerateFiles(string rootPath, int maxEntries) => _files;
    }

    private sealed class ThrowingFileSystemBrowser : IFileSystemBrowser
    {
        private readonly Exception _exception;

        public ThrowingFileSystemBrowser(Exception exception)
        {
            _exception = exception;
        }

        public IReadOnlyList<string> EnumerateDirectories(string rootPath, int maxEntries) => throw _exception;

        public IReadOnlyList<string> EnumerateFiles(string rootPath, int maxEntries) => throw _exception;
    }

    private sealed class FakeRawVolumeBrowser : IRawVolumeBrowser
    {
        private readonly BrowseResult? _result;
        private readonly FileCopyResult? _copyResult;

        public string? LastCopiedFileName { get; private set; }

        public string? LastCopyDestinationDirectory { get; private set; }

        public FakeRawVolumeBrowser(BrowseResult? result = null, FileCopyResult? copyResult = null)
        {
            _result = result;
            _copyResult = copyResult;
        }

        public BrowseResult? TryBrowseRoot(string driveLetter) => _result;

        public FileCopyResult? TryCopyRootFile(string driveLetter, string fileName, string destinationDirectory)
        {
            LastCopiedFileName = fileName;
            LastCopyDestinationDirectory = destinationDirectory;
            return _copyResult;
        }
    }
}