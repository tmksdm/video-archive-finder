namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFolderRefreshService
{
    Task<VideoFolderRefreshResult> RefreshAsync(
        Guid rootSourceId,
        string folderFullPath,
        CancellationToken cancellationToken = default);
}
