using Microsoft.Extensions.Logging;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class SystemStorageVolumeInfoProvider
    : IStorageVolumeInfoProvider
{
    private readonly ILogger<SystemStorageVolumeInfoProvider>
        _logger;

    public SystemStorageVolumeInfoProvider(
        ILogger<SystemStorageVolumeInfoProvider> logger)
    {
        _logger = logger;
    }

    public StorageVolumeInfo? TryGetInfo(
        string path)
    {
        try
        {
            var rootPath = Path.GetPathRoot(
                Path.GetFullPath(path));

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return null;
            }

            var drive = new DriveInfo(rootPath);

            return new StorageVolumeInfo(
                drive.TotalSize,
                drive.AvailableFreeSpace);
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            _logger.LogWarning(
                exception,
                "Could not determine storage volume " +
                "information for thumbnail cache {Path}.",
                path);

            return null;
        }
    }
}
