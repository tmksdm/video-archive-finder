namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileDiscoveryService
{
    Task<VideoFileDiscoveryResult> DiscoverAsync(
        string folderPath,
        CancellationToken cancellationToken = default);
}
