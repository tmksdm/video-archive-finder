namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileMetadata(
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
