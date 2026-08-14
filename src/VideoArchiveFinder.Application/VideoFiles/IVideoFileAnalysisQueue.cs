namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileAnalysisQueue
{
    ValueTask EnqueueAsync(
        VideoFileAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
