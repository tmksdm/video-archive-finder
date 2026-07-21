using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Application.ArchiveSources;

public interface IArchiveSourceService
{
    Task<IReadOnlyList<ArchiveSource>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<AddArchiveSourceResult> AddAsync(
        string fullPath,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default);
}
