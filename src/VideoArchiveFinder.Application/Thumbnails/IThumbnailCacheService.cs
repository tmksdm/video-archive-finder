namespace VideoArchiveFinder.Application.Thumbnails;

public interface IThumbnailCacheService
{
    Task<ThumbnailCacheInfo> GetInfoAsync(
        CancellationToken cancellationToken = default);

    Task<ThumbnailCacheClearResult> ClearAsync(
        CancellationToken cancellationToken = default);
}
