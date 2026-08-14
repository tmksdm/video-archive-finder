namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileAnalysisUpdate(
    Guid RootSourceId,
    string FullPath,
    VideoFileAnalysisState State,
    bool? HasVideoStream,
    TimeSpan? Duration,
    int? Width,
    int? Height,
    string? Codec);
