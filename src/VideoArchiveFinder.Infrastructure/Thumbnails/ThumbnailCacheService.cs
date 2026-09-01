using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class ThumbnailCacheService
    : IThumbnailCacheService
{
    private readonly ThumbnailCachePathProvider
        _cachePathProvider;

    private readonly IStaticThumbnailGenerationQueue
        _thumbnailGenerationQueue;

    private readonly IThumbnailCacheStateRepository
        _cacheStateRepository;

    private readonly IThumbnailCacheMaintenanceService
        _cacheMaintenanceService;

    private readonly ILogger<ThumbnailCacheService>
        _logger;

    public ThumbnailCacheService(
        ThumbnailCachePathProvider cachePathProvider,
        IStaticThumbnailGenerationQueue
            thumbnailGenerationQueue,
        IThumbnailCacheStateRepository
            cacheStateRepository,
        IThumbnailCacheMaintenanceService
            cacheMaintenanceService,
        ILogger<ThumbnailCacheService> logger)
    {
        _cachePathProvider = cachePathProvider;
        _thumbnailGenerationQueue =
            thumbnailGenerationQueue;
        _cacheStateRepository = cacheStateRepository;
        _cacheMaintenanceService =
            cacheMaintenanceService;
        _logger = logger;
    }

    public async Task<ThumbnailCacheInfo>
        GetInfoAsync(
            CancellationToken cancellationToken =
                default)
    {
        var directoryPath =
            _cachePathProvider.GetCacheDirectory();

        var info = await Task.Run(
            () => CalculateInfo(
                directoryPath),
            cancellationToken)
            .ConfigureAwait(false);

        return new ThumbnailCacheInfo(
            DirectoryPath: directoryPath,
            SizeBytes: info.SizeBytes,
            FileCount: info.FileCount,
            MaximumSizeBytes:
                _cacheMaintenanceService
                    .GetMaximumSizeBytes(
                        info.SizeBytes));
    }

    public async Task<ThumbnailCacheClearResult>
        ClearAsync(
            CancellationToken cancellationToken =
                default)
    {
        await _thumbnailGenerationQueue
            .WaitForIdleAsync(cancellationToken)
            .ConfigureAwait(false);

        var resetFileCount =
            await _cacheStateRepository
                .ResetAllAsync(cancellationToken)
                .ConfigureAwait(false);

        var directoryPath =
            _cachePathProvider.GetCacheDirectory();

        var deletionResult = await Task.Run(
            () => DeleteCacheFiles(
                directoryPath,
                cancellationToken),
            cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Thumbnail cache cleared: " +
            "{DeletedFileCount} files " +
            "({DeletedSizeBytes} bytes) deleted; " +
            "{ResetVideoFileCount} indexed video " +
            "files reset to the not generated state.",
            deletionResult.DeletedFileCount,
            deletionResult.DeletedSizeBytes,
            resetFileCount);

        return new ThumbnailCacheClearResult(
            DeletedSizeBytes:
                deletionResult.DeletedSizeBytes,
            DeletedFileCount:
                deletionResult.DeletedFileCount);
    }

    private static (
        long SizeBytes,
        long FileCount) CalculateInfo(
            string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return (0, 0);
        }

        long sizeBytes = 0;
        long fileCount = 0;

        foreach (var filePath in
            Directory.EnumerateFiles(
                directoryPath,
                "*",
                SearchOption.AllDirectories))
        {
            sizeBytes +=
                new FileInfo(filePath).Length;

            fileCount++;
        }

        return (sizeBytes, fileCount);
    }

    private static (
        long DeletedSizeBytes,
        long DeletedFileCount)
        DeleteCacheFiles(
            string directoryPath,
            CancellationToken cancellationToken)
    {
        long deletedSizeBytes = 0;
        long deletedFileCount = 0;

        if (!Directory.Exists(directoryPath))
        {
            return (deletedSizeBytes,
                deletedFileCount);
        }

        foreach (var filePath in
            Directory.EnumerateFiles(
                directoryPath,
                "*",
                SearchOption.AllDirectories))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                var file = new FileInfo(filePath);

                var sizeBytes = file.Length;

                file.Delete();

                deletedSizeBytes += sizeBytes;
                deletedFileCount++;
            }
            catch (Exception exception)
                when (exception is IOException or
                    UnauthorizedAccessException)
            {
                // Файл мог быть занят другим процессом.
            }
        }

        foreach (var subDirectoryPath in
            Directory
                .EnumerateDirectories(
                    directoryPath,
                    "*",
                    SearchOption.AllDirectories)
                .OrderByDescending(
                    path => path.Length))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                Directory.Delete(subDirectoryPath);
            }
            catch (Exception exception)
                when (exception is IOException or
                    UnauthorizedAccessException)
            {
                // Папка не пуста или занята другим процессом.
            }
        }

        return (deletedSizeBytes,
            deletedFileCount);
    }
}
