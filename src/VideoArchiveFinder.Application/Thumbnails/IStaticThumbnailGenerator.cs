namespace VideoArchiveFinder.Application.Thumbnails;

public interface IStaticThumbnailGenerator
{
    Task<StaticThumbnailGenerationResult> GenerateAsync(
        StaticThumbnailRequest request,
        CancellationToken cancellationToken = default);
}
