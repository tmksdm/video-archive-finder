namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public interface IStorageVolumeInfoProvider
{
    StorageVolumeInfo? TryGetInfo(
        string path);
}
