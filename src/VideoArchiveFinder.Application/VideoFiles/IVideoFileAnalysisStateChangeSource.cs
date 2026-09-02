namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileAnalysisStateChangeSource
{
    event EventHandler<
        VideoFileAnalysisStateChangedEventArgs>?
        StateChanged;
}
