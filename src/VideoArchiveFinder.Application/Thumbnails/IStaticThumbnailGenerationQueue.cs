namespace VideoArchiveFinder.Application.Thumbnails;

public interface IStaticThumbnailGenerationQueue
{
    ValueTask EnqueueAsync(
        StaticThumbnailRequest request,
        CancellationToken cancellationToken = default);

    Task WaitForIdleAsync(
        CancellationToken cancellationToken = default);
}
