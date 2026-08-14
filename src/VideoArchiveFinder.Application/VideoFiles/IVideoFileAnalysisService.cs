namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileAnalysisService
{
    Task<VideoFileAnalysisResult> AnalyzeAsync(
        Guid rootSourceId,
        string fullPath,
        CancellationToken cancellationToken = default);
}
