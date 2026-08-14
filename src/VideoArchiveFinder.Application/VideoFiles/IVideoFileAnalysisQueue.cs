namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileAnalysisQueue
{
    ValueTask EnqueueAsync(
        Guid rootSourceId,
        string fullPath,
        CancellationToken cancellationToken = default);
}
