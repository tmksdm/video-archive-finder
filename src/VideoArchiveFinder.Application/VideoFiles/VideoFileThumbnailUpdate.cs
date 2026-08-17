namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileThumbnailUpdate(
    Guid RootSourceId,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    VideoFileThumbnailState State,
    string? ThumbnailPath);
