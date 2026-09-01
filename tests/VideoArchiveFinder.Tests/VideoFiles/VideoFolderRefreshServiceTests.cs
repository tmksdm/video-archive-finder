using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFolderRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_IndexesRequestedFolderAndReturnsFiles()
    {
        var rootSourceId = Guid.NewGuid();
        var folderPath = @"C:\Archive\Видео";
        var filePath = folderPath + @"\ЁЖ_Дорога.MP4";

        var discoveryService =
            new RecordingDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files:
                    [
                        new DiscoveredVideoFile(
                            FullPath: filePath,
                            Name: "ЁЖ_Дорога.MP4",
                            Extension: ".mp4",
                            SizeBytes: 12_345,
                            LastWriteTimeUtc:
                                DateTimeOffset.UtcNow)
                    ],
                    ErrorCount: 0,
                    CanRemoveStaleEntries: true));

        var repository =
            new RecordingRepository(
            [
                CreateIndexedFile(
                    rootSourceId,
                    folderPath,
                    filePath)
            ]);

        var service =
            CreateService(
                discoveryService,
                repository);

        var result =
            await service.RefreshAsync(
                rootSourceId,
                folderPath);

        Assert.Equal(
            folderPath,
            Assert.Single(
                discoveryService.RequestedFolders));

        var indexedItem =
            Assert.Single(
                Assert.Single(
                    repository.Batches));

        Assert.Equal(filePath, indexedItem.FullPath);
        Assert.Equal(
            "еж_дорога.mp4",
            indexedItem.NormalizedName);

        var completion =
            Assert.Single(repository.Completions);

        Assert.Equal(
            rootSourceId,
            completion.RootSourceId);

        Assert.Equal(
            folderPath,
            completion.FolderFullPath);

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.ErrorCount);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task RefreshAsync_WhenDiscoveryIsIncomplete_PreservesCache()
    {
        var rootSourceId = Guid.NewGuid();
        var folderPath = @"C:\Archive\Видео";

        var discoveryService =
            new RecordingDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files: [],
                    ErrorCount: 1,
                    CanRemoveStaleEntries: false));

        var repository =
            new RecordingRepository(
            [
                CreateIndexedFile(
                    rootSourceId,
                    folderPath,
                    folderPath + @"\Кэш.mp4")
            ]);

        var service =
            CreateService(
                discoveryService,
                repository);

        var result =
            await service.RefreshAsync(
                rootSourceId,
                folderPath);

        Assert.Empty(repository.Completions);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.ErrorCount);
        Assert.Single(result.Files);
    }

    private static VideoFolderRefreshService CreateService(
        IVideoFileDiscoveryService discoveryService,
        IVideoFileIndexRepository repository)
    {
        return new VideoFolderRefreshService(
            discoveryService,
            repository,
            new TextNormalizationService(),
            NullLogger<VideoFolderRefreshService>.Instance);
    }

    private static IndexedVideoFile CreateIndexedFile(
        Guid rootSourceId,
        string folderPath,
        string filePath)
    {
        return new IndexedVideoFile(
            Id: 1,
            FullPath: filePath,
            Name: Path.GetFileName(filePath),
            NormalizedName:
                Path.GetFileName(filePath)
                    .ToLowerInvariant(),
            Extension: ".mp4",
            SizeBytes: 12_345,
            LastWriteTimeUtc:
                DateTimeOffset.UtcNow,
            FolderFullPath: folderPath,
            RootSourceId: rootSourceId,
            IsAvailable: true);
    }

    private sealed class RecordingDiscoveryService
        : IVideoFileDiscoveryService
    {
        private readonly VideoFileDiscoveryResult _result;

        public RecordingDiscoveryService(
            VideoFileDiscoveryResult result)
        {
            _result = result;
        }

        public List<string> RequestedFolders
        {
            get;
        } = [];

        public Task<VideoFileDiscoveryResult> DiscoverAsync(
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedFolders.Add(folderPath);

            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingRepository
        : IVideoFileIndexRepository
    {
        private readonly IReadOnlyList<IndexedVideoFile>
            _files;

        public RecordingRepository(
            IReadOnlyList<IndexedVideoFile> files)
        {
            _files = files;
        }

        public List<
            IReadOnlyList<VideoFileIndexUpsertItem>> Batches
        {
            get;
        } = [];

        public List<Completion> Completions
        {
            get;
        } = [];

        public Task UpsertBatchAsync(
            IReadOnlyCollection<VideoFileIndexUpsertItem> files,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Batches.Add(files.ToArray());

            return Task.CompletedTask;
        }

        public Task<int> CompleteFolderScanAsync(
            Guid rootSourceId,
            string folderFullPath,
            DateTimeOffset scanStartedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Completions.Add(
                new Completion(
                    rootSourceId,
                    folderFullPath));

            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<IndexedVideoFile>>
            GetByFolderPathAsync(
                Guid rootSourceId,
                string folderFullPath,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_files);
        }

        public Task<bool> UpdateAnalysisAsync(
            VideoFileAnalysisUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> UpdateThumbnailAsync(
            VideoFileThumbnailUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed record Completion(
        Guid RootSourceId,
        string FolderFullPath);
}
