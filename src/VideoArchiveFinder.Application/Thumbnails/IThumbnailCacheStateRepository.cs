namespace VideoArchiveFinder.Application.Thumbnails;

public interface IThumbnailCacheStateRepository
{
    Task<int> ResetPathsAsync(
        IReadOnlyCollection<string> thumbnailPaths,
        CancellationToken cancellationToken = default);

    Task<int> ResetAllAsync(
        CancellationToken cancellationToken = default);
}
