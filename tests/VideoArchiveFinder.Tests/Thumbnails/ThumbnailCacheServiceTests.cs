using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class ThumbnailCacheServiceTests
{
    [Fact]
    public async Task GetInfoAsync_SumsFilesAcrossShardDirectories()
    {
        using var temporaryLocation =
            new TemporaryCacheLocation();

        var shardDirectory =
            temporaryLocation.CreateShardDirectory("ab");

        WriteFile(
            Path.Combine(shardDirectory, "ab01.jpg"),
            100);

        WriteFile(
            Path.Combine(shardDirectory, "ab02.jpg"),
            250);

        var secondShardDirectory =
            temporaryLocation.CreateShardDirectory("cd");

        WriteFile(
            Path.Combine(secondShardDirectory, "cd03.jpg"),
            52);

        var service = CreateService(
            temporaryLocation,
            new RecordingThumbnailGenerationQueue([]),
            new RecordingThumbnailCacheStateRepository([]));

        var cacheInfo =
            await service.GetInfoAsync();

        Assert.Equal(402, cacheInfo.SizeBytes);
        Assert.Equal(3, cacheInfo.FileCount);
        Assert.Equal(5_000, cacheInfo.MaximumSizeBytes);
    }

    [Fact]
    public async Task ClearAsync_DeletesFilesResetsStatesAndKeepsRoot()
    {
        using var temporaryLocation =
            new TemporaryCacheLocation();

        var shardDirectory =
            temporaryLocation.CreateShardDirectory("ef");

        WriteFile(
            Path.Combine(shardDirectory, "ef01.jpg"),
            128);

        WriteFile(
            Path.Combine(shardDirectory, "ef02.jpg"),
            64);

        var operationOrder = new List<string>();

        var queue =
            new RecordingThumbnailGenerationQueue(
                operationOrder);

        var stateRepository =
            new RecordingThumbnailCacheStateRepository(
                operationOrder);

        var service = CreateService(
            temporaryLocation,
            queue,
            stateRepository);

        var clearResult =
            await service.ClearAsync();

        Assert.Equal(192, clearResult.DeletedSizeBytes);
        Assert.Equal(2, clearResult.DeletedFileCount);

        Assert.False(Directory.Exists(shardDirectory));
        Assert.True(Directory.Exists(
            temporaryLocation.CacheRootDirectory));

        Assert.Empty(Directory.EnumerateFileSystemEntries(
            temporaryLocation.CacheRootDirectory));

        Assert.Equal(
            ["WaitForIdle", "ResetAll"],
            operationOrder);
    }

    [Fact]
    public async Task ClearAsync_MissingCacheDirectory_StillResetsStates()
    {
        using var temporaryLocation =
            new TemporaryCacheLocation();

        var operationOrder = new List<string>();

        var service = CreateService(
            temporaryLocation,
            new RecordingThumbnailGenerationQueue(
                operationOrder),
            new RecordingThumbnailCacheStateRepository(
                operationOrder));

        var clearResult =
            await service.ClearAsync();

        Assert.Equal(0, clearResult.DeletedSizeBytes);
        Assert.Equal(0, clearResult.DeletedFileCount);

        Assert.Equal(
            ["WaitForIdle", "ResetAll"],
            operationOrder);
    }

    private static ThumbnailCacheService
        CreateService(
            TemporaryCacheLocation temporaryLocation,
            RecordingThumbnailGenerationQueue queue,
            RecordingThumbnailCacheStateRepository
                stateRepository)
    {
        return new ThumbnailCacheService(
            new ThumbnailCachePathProvider(
                new FixedApplicationDataDirectoryProvider(
                    temporaryLocation.ApplicationRootDirectory),
                new ThumbnailCacheKeyGenerator()),
            queue,
            stateRepository,
            new FixedThumbnailCacheMaintenanceService(),
            NullLogger<ThumbnailCacheService>.Instance);
    }

    private static void WriteFile(
        string filePath,
        int lengthBytes)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(filePath)!);

        File.WriteAllBytes(
            filePath,
            new byte[lengthBytes]);
    }

    private sealed class
        FixedApplicationDataDirectoryProvider
        : IApplicationDataDirectoryProvider
    {
        private readonly string _directory;

        public FixedApplicationDataDirectoryProvider(
            string directory)
        {
            _directory = directory;
        }

        public string GetApplicationDataDirectory()
        {
            return _directory;
        }
    }

    private sealed class
        RecordingThumbnailGenerationQueue
        : IStaticThumbnailGenerationQueue
    {
        private readonly List<string> _operationOrder;

        public RecordingThumbnailGenerationQueue(
            List<string> operationOrder)
        {
            _operationOrder = operationOrder;
        }

        public ValueTask EnqueueAsync(
            StaticThumbnailRequest request,
            CancellationToken cancellationToken =
                default)
        {
            throw new NotSupportedException();
        }

        public Task WaitForIdleAsync(
            CancellationToken cancellationToken =
                default)
        {
            _operationOrder.Add("WaitForIdle");

            return Task.CompletedTask;
        }
    }

    private sealed class
        RecordingThumbnailCacheStateRepository
        : IThumbnailCacheStateRepository
    {
        private readonly List<string> _operationOrder;

        public RecordingThumbnailCacheStateRepository(
            List<string> operationOrder)
        {
            _operationOrder = operationOrder;
        }

        public int ResetCallCount { get; private set; }

        public Task<int> ResetPathsAsync(
            IReadOnlyCollection<string> thumbnailPaths,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<int> ResetAllAsync(
            CancellationToken cancellationToken =
                default)
        {
            _operationOrder.Add("ResetAll");
            ResetCallCount++;

            return Task.FromResult(0);
        }
    }

    private sealed class
        FixedThumbnailCacheMaintenanceService
        : IThumbnailCacheMaintenanceService
    {
        public long? GetMaximumSizeBytes(
            long currentCacheSizeBytes)
        {
            return 5_000;
        }

        public Task<ThumbnailCacheTrimResult> TrimAsync(
            string? protectedFilePath = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new ThumbnailCacheTrimResult(
                    5_000,
                    0,
                    0,
                    0,
                    0));
        }
    }

    private sealed class TemporaryCacheLocation
        : IDisposable
    {
        public string ApplicationRootDirectory { get; }

        public string CacheRootDirectory =>
            Path.Combine(
                ApplicationRootDirectory,
                "Cache",
                "Thumbnails");

        public TemporaryCacheLocation()
        {
            ApplicationRootDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    $"vaf-cache-tests-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                ApplicationRootDirectory);
        }

        public string CreateShardDirectory(
            string name)
        {
            return Directory.CreateDirectory(
                Path.Combine(
                    CacheRootDirectory,
                    "v1",
                    name)).FullName;
        }

        public void Dispose()
        {
            Directory.Delete(
                ApplicationRootDirectory,
                recursive: true);
        }
    }
}
