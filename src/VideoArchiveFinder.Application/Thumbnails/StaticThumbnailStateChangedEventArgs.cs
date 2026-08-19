using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Application.Thumbnails;

public sealed class StaticThumbnailStateChangedEventArgs
    : EventArgs
{
    public StaticThumbnailStateChangedEventArgs(
        StaticThumbnailRequest request,
        VideoFileThumbnailState state,
        string? thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (state == VideoFileThumbnailState.Succeeded &&
            string.IsNullOrWhiteSpace(thumbnailPath))
        {
            throw new ArgumentException(
                "A succeeded thumbnail requires a path.",
                nameof(thumbnailPath));
        }

        Request = request;
        State = state;
        ThumbnailPath =
            state == VideoFileThumbnailState.Succeeded
                ? thumbnailPath
                : null;
    }

    public StaticThumbnailRequest Request { get; }

    public VideoFileThumbnailState State { get; }

    public string? ThumbnailPath { get; }
}
