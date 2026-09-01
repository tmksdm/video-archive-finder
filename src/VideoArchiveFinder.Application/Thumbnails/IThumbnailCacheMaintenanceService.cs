namespace VideoArchiveFinder.Application.Thumbnails;

public interface IThumbnailCacheMaintenanceService
{
    long? GetMaximumSizeBytes(
        long currentCacheSizeBytes);

    Task<ThumbnailCacheTrimResult> TrimAsync(
        string? protectedFilePath = null,
        CancellationToken cancellationToken = default);
}
