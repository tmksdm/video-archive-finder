namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record StaticThumbnailRequest(
    string VideoPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
