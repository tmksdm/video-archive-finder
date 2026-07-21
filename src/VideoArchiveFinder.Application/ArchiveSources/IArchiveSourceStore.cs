using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Application.ArchiveSources;

public interface IArchiveSourceStore
{
    Task<IReadOnlyList<ArchiveSource>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IEnumerable<ArchiveSource> sources,
        CancellationToken cancellationToken = default);
}
