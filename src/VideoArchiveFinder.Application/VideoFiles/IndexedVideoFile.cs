namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record IndexedVideoFile(
    long Id,
    string FullPath,
    string Name,
    string NormalizedName,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string FolderFullPath,
    Guid RootSourceId,
    bool IsAvailable,
    bool? HasVideoStream = null,
    TimeSpan? Duration = null,
    int? Width = null,
    int? Height = null,
    string? Codec = null,
    VideoFileAnalysisState AnalysisState =
        VideoFileAnalysisState.NotAnalyzed);
