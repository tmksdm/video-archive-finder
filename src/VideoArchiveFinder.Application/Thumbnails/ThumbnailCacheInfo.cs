namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record ThumbnailCacheInfo(
    string DirectoryPath,
    long SizeBytes,
    long FileCount,
    long? MaximumSizeBytes);
