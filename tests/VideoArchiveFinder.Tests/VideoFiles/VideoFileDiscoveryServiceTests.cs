using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFileDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsOnlySupportedCandidates()
    {
        var fileSystem =
            new TestVideoFileSystem(
                files:
                [
                    @"C:\Archive\Folder\First.mp4",
                    @"C:\Archive\Folder\Document.txt",
                    @"C:\Archive\Folder\Second.MOV"
                ]);

        fileSystem.AddMetadata(
            @"C:\Archive\Folder\First.mp4",
            sizeBytes: 1_000);

        fileSystem.AddMetadata(
            @"C:\Archive\Folder\Second.MOV",
            sizeBytes: 2_000);

        var service = CreateService(fileSystem);

        var result =
            await service.DiscoverAsync(
                @"C:\Archive\Folder");

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.CanRemoveStaleEntries);

        Assert.Equal(
            "First.mp4",
            result.Files[0].Name);

        Assert.Equal(
            ".mp4",
            result.Files[0].Extension);

        Assert.Equal(
            1_000,
            result.Files[0].SizeBytes);

        Assert.Equal(
            "Second.MOV",
            result.Files[1].Name);

        Assert.Equal(
            ".mov",
            result.Files[1].Extension);

        Assert.Equal(
            2_000,
            result.Files[1].SizeBytes);

        Assert.DoesNotContain(
            result.Files,
            file => file.Name == "Document.txt");
    }

    [Fact]
    public async Task DiscoverAsync_WhenFileFails_ContinuesWithoutCleanupPermission()
    {
        var fileSystem =
            new TestVideoFileSystem(
                files:
                [
                    @"C:\Archive\Folder\Broken.mp4",
                    @"C:\Archive\Folder\Working.mkv"
                ]);

        fileSystem.AddMetadataError(
            @"C:\Archive\Folder\Broken.mp4",
            new IOException("Cannot read file."));

        fileSystem.AddMetadata(
            @"C:\Archive\Folder\Working.mkv",
            sizeBytes: 3_000);

        var service = CreateService(fileSystem);

        var result =
            await service.DiscoverAsync(
                @"C:\Archive\Folder");

        Assert.Equal(1, result.ErrorCount);
        Assert.False(result.CanRemoveStaleEntries);

        var discoveredFile =
            Assert.Single(result.Files);

        Assert.Equal(
            "Working.mkv",
            discoveredFile.Name);

        Assert.Equal(
            3_000,
            discoveredFile.SizeBytes);
    }

    [Fact]
    public async Task DiscoverAsync_WhenFolderFails_ReturnsSafeFailureResult()
    {
        var fileSystem =
            new TestVideoFileSystem(
                files: [],
                enumerationException:
                    new UnauthorizedAccessException(
                        "Access denied."));

        var service = CreateService(fileSystem);

        var result =
            await service.DiscoverAsync(
                @"C:\Archive\Protected");

        Assert.Empty(result.Files);
        Assert.Equal(1, result.ErrorCount);
        Assert.False(result.CanRemoveStaleEntries);
    }

    private static VideoFileDiscoveryService
        CreateService(
            IVideoFileSystem fileSystem)
    {
        return new VideoFileDiscoveryService(
            fileSystem,
            new VideoFileCandidatePolicy(),
            NullLogger<
                VideoFileDiscoveryService>.Instance);
    }

    private sealed class TestVideoFileSystem
        : IVideoFileSystem
    {
        private readonly IReadOnlyList<string>
            _files;

        private readonly Exception?
            _enumerationException;

        private readonly Dictionary<
            string,
            VideoFileMetadata> _metadata =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<
            string,
            Exception> _metadataErrors =
                new(
                    StringComparer.OrdinalIgnoreCase);

        public TestVideoFileSystem(
            IReadOnlyList<string> files,
            Exception? enumerationException = null)
        {
            _files = files;
            _enumerationException =
                enumerationException;
        }

        public IReadOnlyList<string> GetFiles(
            string folderPath)
        {
            if (_enumerationException is not null)
            {
                throw _enumerationException;
            }

            return _files;
        }

        public VideoFileMetadata GetMetadata(
            string filePath)
        {
            if (_metadataErrors.TryGetValue(
                filePath,
                out var exception))
            {
                throw exception;
            }

            return _metadata[filePath];
        }

        public void AddMetadata(
            string filePath,
            long sizeBytes)
        {
            _metadata[filePath] =
                new VideoFileMetadata(
                    SizeBytes: sizeBytes,
                    LastWriteTimeUtc:
                        new DateTimeOffset(
                            2026,
                            7,
                            31,
                            10,
                            0,
                            0,
                            TimeSpan.Zero));
        }

        public void AddMetadataError(
            string filePath,
            Exception exception)
        {
            _metadataErrors[filePath] =
                exception;
        }
    }
}
