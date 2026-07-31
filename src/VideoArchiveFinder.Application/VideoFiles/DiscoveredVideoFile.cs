namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record DiscoveredVideoFile(
    string FullPath,
    string Name,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
