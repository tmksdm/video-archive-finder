using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class ThumbnailCacheMaintenanceService
    : IThumbnailCacheMaintenanceService,
      IDisposable
{
    private static readonly TimeSpan RecentFileGracePeriod =
        TimeSpan.FromMinutes(1);

    private readonly ThumbnailCachePathProvider
        _cachePathProvider;

    private readonly IStorageVolumeInfoProvider
        _volumeInfoProvider;

    private readonly ThumbnailCacheLimitCalculator
        _limitCalculator;

    private readonly IThumbnailCacheStateRepository
        _cacheStateRepository;

    private readonly ILogger<ThumbnailCacheMaintenanceService>
        _logger;

    private readonly SemaphoreSlim _trimLock = new(1, 1);

    public ThumbnailCacheMaintenanceService(
        ThumbnailCachePathProvider cachePathProvider,
        IStorageVolumeInfoProvider volumeInfoProvider,
        ThumbnailCacheLimitCalculator limitCalculator,
        IThumbnailCacheStateRepository cacheStateRepository,
        ILogger<ThumbnailCacheMaintenanceService> logger)
    {
        _cachePathProvider = cachePathProvider;
        _volumeInfoProvider = volumeInfoProvider;
        _limitCalculator = limitCalculator;
        _cacheStateRepository = cacheStateRepository;
        _logger = logger;
    }

    public long? GetMaximumSizeBytes(
        long currentCacheSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            currentCacheSizeBytes);

        var cacheDirectory =
            _cachePathProvider.GetCacheDirectory();

        var volumeInfo =
            _volumeInfoProvider.TryGetInfo(
                cacheDirectory);

        if (volumeInfo is null)
        {
            return null;
        }

        return _limitCalculator.CalculateMaximumSizeBytes(
            volumeInfo.TotalSizeBytes,
            volumeInfo.AvailableFreeSpaceBytes,
            currentCacheSizeBytes);
    }

    public async Task<ThumbnailCacheTrimResult> TrimAsync(
        string? protectedFilePath = null,
        CancellationToken cancellationToken = default)
    {
        await _trimLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var cacheDirectory =
                _cachePathProvider.GetCacheDirectory();

            var cacheFiles = await Task.Run(
                    () => ReadCacheFiles(
                        cacheDirectory,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            var sizeBeforeBytes = cacheFiles.Sum(
                file => file.SizeBytes);

            var maximumSizeBytes =
                GetMaximumSizeBytes(sizeBeforeBytes);

            if (maximumSizeBytes is null ||
                sizeBeforeBytes <= maximumSizeBytes.Value)
            {
                return new ThumbnailCacheTrimResult(
                    maximumSizeBytes ?? sizeBeforeBytes,
                    sizeBeforeBytes,
                    sizeBeforeBytes,
                    0,
                    0);
            }

            var cleanupTargetBytes =
                ThumbnailCacheLimitCalculator
                    .CalculateCleanupTargetBytes(
                        maximumSizeBytes.Value);

            var deletionResult = await Task.Run(
                    () => DeleteOldestFiles(
                        cacheFiles,
                        sizeBeforeBytes,
                        cleanupTargetBytes,
                        protectedFilePath,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (deletionResult.DeletedPaths.Count > 0)
            {
                await _cacheStateRepository
                    .ResetPathsAsync(
                        deletionResult.DeletedPaths,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Thumbnail cache automatic cleanup: " +
                "{DeletedFileCount} files " +
                "({DeletedSizeBytes} bytes) deleted; " +
                "size changed from {SizeBeforeBytes} to " +
                "{SizeAfterBytes}; limit is " +
                "{MaximumSizeBytes} bytes.",
                deletionResult.DeletedPaths.Count,
                deletionResult.DeletedSizeBytes,
                sizeBeforeBytes,
                deletionResult.SizeAfterBytes,
                maximumSizeBytes.Value);

            return new ThumbnailCacheTrimResult(
                maximumSizeBytes.Value,
                sizeBeforeBytes,
                deletionResult.SizeAfterBytes,
                deletionResult.DeletedSizeBytes,
                deletionResult.DeletedPaths.Count);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Automatic thumbnail cache cleanup failed. " +
                "Thumbnail generation will continue.");

            return new ThumbnailCacheTrimResult(
                0,
                0,
                0,
                0,
                0);
        }
        finally
        {
            _trimLock.Release();
        }
    }

    private static IReadOnlyList<CacheFile> ReadCacheFiles(
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return [];
        }

        var files = new List<CacheFile>();

        foreach (var filePath in Directory.EnumerateFiles(
                     cacheDirectory,
                     "*.jpg",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filePath.EndsWith(
                    ".tmp.jpg",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var file = new FileInfo(filePath);

                files.Add(
                    new CacheFile(
                        file.FullName,
                        file.Length,
                        file.LastWriteTimeUtc));
            }
            catch (Exception exception)
                when (exception is IOException or
                    UnauthorizedAccessException)
            {
                // A file can disappear while the cache is scanned.
            }
        }

        return files;
    }

    private static DeletionResult DeleteOldestFiles(
        IReadOnlyList<CacheFile> files,
        long sizeBeforeBytes,
        long cleanupTargetBytes,
        string? protectedFilePath,
        CancellationToken cancellationToken)
    {
        var deletedPaths = new List<string>();
        long deletedSizeBytes = 0;
        var recentThresholdUtc =
            DateTime.UtcNow - RecentFileGracePeriod;

        foreach (var file in files
                     .Where(
                         file => file.LastWriteTimeUtc <
                                 recentThresholdUtc)
                     .OrderBy(file => file.LastWriteTimeUtc)
                     .ThenBy(
                         file => file.Path,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sizeBeforeBytes - deletedSizeBytes <=
                    cleanupTargetBytes ||
                string.Equals(
                    file.Path,
                    protectedFilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(file.Path);
                deletedPaths.Add(file.Path);
                deletedSizeBytes += file.SizeBytes;
            }
            catch (Exception exception)
                when (exception is IOException or
                    UnauthorizedAccessException)
            {
                // A busy or inaccessible file is retried next time.
            }
        }

        return new DeletionResult(
            Math.Max(
                0,
                sizeBeforeBytes - deletedSizeBytes),
            deletedSizeBytes,
            deletedPaths);
    }

    public void Dispose()
    {
        _trimLock.Dispose();
    }

    private sealed record CacheFile(
        string Path,
        long SizeBytes,
        DateTime LastWriteTimeUtc);

    private sealed record DeletionResult(
        long SizeAfterBytes,
        long DeletedSizeBytes,
        IReadOnlyCollection<string> DeletedPaths);
}
