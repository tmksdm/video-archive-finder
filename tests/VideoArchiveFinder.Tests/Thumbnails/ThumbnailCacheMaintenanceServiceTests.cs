using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class ThumbnailCacheMaintenanceServiceTests
    : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "vaf-cache-maintenance-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TrimAsync_DeletesOldestFilesToNinetyPercentOfLimit()
    {
        var paths = CreateCacheFiles(
            ("oldest.jpg", 500, -30),
            ("middle.jpg", 300, -20),
            ("newest.jpg", 300, -10));

        var stateRepository =
            new RecordingThumbnailCacheStateRepository();

        using var service = CreateService(
            stateRepository,
            totalSizeBytes: 20_000,
            availableFreeSpaceBytes: 10_000);

        var result = await service.TrimAsync();

        Assert.Equal(1_000, result.MaximumSizeBytes);
        Assert.Equal(1_100, result.SizeBeforeBytes);
        Assert.Equal(600, result.SizeAfterBytes);
        Assert.Equal(500, result.DeletedSizeBytes);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.False(File.Exists(paths[0]));
        Assert.True(File.Exists(paths[1]));
        Assert.True(File.Exists(paths[2]));
        Assert.Equal([paths[0]], stateRepository.ResetPaths);
    }

    [Fact]
    public async Task TrimAsync_DoesNotDeleteProtectedOrRecentFiles()
    {
        var paths = CreateCacheFiles(
            ("protected.jpg", 700, -30),
            ("recent.jpg", 700, 0));

        using var service = CreateService(
            new RecordingThumbnailCacheStateRepository(),
            totalSizeBytes: 20_000,
            availableFreeSpaceBytes: 10_000);

        var result = await service.TrimAsync(
            protectedFilePath: paths[0]);

        Assert.Equal(1_400, result.SizeAfterBytes);
        Assert.Equal(0, result.DeletedFileCount);
        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    private ThumbnailCacheMaintenanceService CreateService(
        RecordingThumbnailCacheStateRepository stateRepository,
        long totalSizeBytes,
        long availableFreeSpaceBytes)
    {
        var pathProvider = new ThumbnailCachePathProvider(
            new FixedApplicationDataDirectoryProvider(
                _temporaryDirectory),
            new ThumbnailCacheKeyGenerator());

        return new ThumbnailCacheMaintenanceService(
            pathProvider,
            new FixedStorageVolumeInfoProvider(
                new StorageVolumeInfo(
                    totalSizeBytes,
                    availableFreeSpaceBytes)),
            new ThumbnailCacheLimitCalculator(
                maximumSizeBytes: 1_000,
                minimumFreeSpaceReserveBytes: 1_000),
            stateRepository,
            NullLogger<
                ThumbnailCacheMaintenanceService>.Instance);
    }

    private string[] CreateCacheFiles(
        params (string Name, int Size, int AgeMinutes)[] files)
    {
        var directory = Path.Combine(
            _temporaryDirectory,
            "Cache",
            "Thumbnails",
            "v1",
            "aa");

        Directory.CreateDirectory(directory);

        return files.Select(
                file =>
                {
                    var path = Path.Combine(directory, file.Name);
                    File.WriteAllBytes(path, new byte[file.Size]);
                    File.SetLastWriteTimeUtc(
                        path,
                        DateTime.UtcNow.AddMinutes(
                            file.AgeMinutes));
                    return path;
                })
            .ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private sealed class FixedApplicationDataDirectoryProvider(
        string directory)
        : IApplicationDataDirectoryProvider
    {
        public string GetApplicationDataDirectory()
        {
            return directory;
        }
    }

    private sealed class FixedStorageVolumeInfoProvider(
        StorageVolumeInfo volumeInfo)
        : IStorageVolumeInfoProvider
    {
        public StorageVolumeInfo? TryGetInfo(string path)
        {
            return volumeInfo;
        }
    }

    private sealed class RecordingThumbnailCacheStateRepository
        : IThumbnailCacheStateRepository
    {
        public IReadOnlyCollection<string> ResetPaths
        {
            get;
            private set;
        } = [];

        public Task<int> ResetPathsAsync(
            IReadOnlyCollection<string> thumbnailPaths,
            CancellationToken cancellationToken = default)
        {
            ResetPaths = thumbnailPaths.ToArray();
            return Task.FromResult(thumbnailPaths.Count);
        }

        public Task<int> ResetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
