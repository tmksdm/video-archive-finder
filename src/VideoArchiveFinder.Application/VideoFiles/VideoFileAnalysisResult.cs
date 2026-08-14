namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileAnalysisResult(
    bool WasStored,
    VideoFileAnalysisState State,
    string DiagnosticMessage);
