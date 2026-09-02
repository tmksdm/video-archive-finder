namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileAnalysisResult(
    bool WasStored,
    VideoFileAnalysisState State,
    bool? HasVideoStream,
    string DiagnosticMessage,
    TimeSpan? Duration = null,
    int? Width = null,
    int? Height = null,
    string? Codec = null);
