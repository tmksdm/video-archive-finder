namespace VideoArchiveFinder.Application.Thumbnails;

public interface IStaticThumbnailStateChangeSource
{
    event EventHandler<
        StaticThumbnailStateChangedEventArgs>?
        StateChanged;
}
