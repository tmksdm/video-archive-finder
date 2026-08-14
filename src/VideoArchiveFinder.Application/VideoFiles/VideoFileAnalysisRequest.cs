namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileAnalysisRequest(
    Guid RootSourceId,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
