namespace VideoArchiveFinder.Application.ArchiveSources;

public interface IArchiveSourceAvailabilityChecker
{
    Task<ArchiveSourceAvailability> CheckAsync(
        string fullPath,
        CancellationToken cancellationToken = default);
}
