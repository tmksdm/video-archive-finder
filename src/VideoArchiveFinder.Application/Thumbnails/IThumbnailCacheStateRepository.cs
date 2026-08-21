namespace VideoArchiveFinder.Application.Thumbnails;

public interface IThumbnailCacheStateRepository
{
    Task<int> ResetAllAsync(
        CancellationToken cancellationToken = default);
}
