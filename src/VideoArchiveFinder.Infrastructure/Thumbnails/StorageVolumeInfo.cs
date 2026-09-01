namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed record StorageVolumeInfo(
    long TotalSizeBytes,
    long AvailableFreeSpaceBytes);
