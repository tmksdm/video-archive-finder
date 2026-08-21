namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record ThumbnailCacheClearResult(
    long DeletedSizeBytes,
    long DeletedFileCount);
