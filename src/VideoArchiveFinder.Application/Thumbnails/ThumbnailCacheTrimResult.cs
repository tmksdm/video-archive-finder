namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record ThumbnailCacheTrimResult(
    long MaximumSizeBytes,
    long SizeBeforeBytes,
    long SizeAfterBytes,
    long DeletedSizeBytes,
    long DeletedFileCount);
