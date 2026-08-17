namespace VideoArchiveFinder.Application.Thumbnails;

public sealed record StaticThumbnailRequest(
    Guid RootSourceId,
    string VideoPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
