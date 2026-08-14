namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record StaticThumbnailGenerationResult(
    StaticThumbnailGenerationStatus Status,
    string? ThumbnailPath,
    int? ExitCode,
    string DiagnosticMessage)
{
    public bool IsSuccess =>
        Status is
            StaticThumbnailGenerationStatus.Generated or
            StaticThumbnailGenerationStatus.CacheHit;
}
