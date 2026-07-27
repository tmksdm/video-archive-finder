using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderIndexingService
{
    Task<FolderIndexingResult> ScanAsync(
        ArchiveSource source,
        IProgress<FolderIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
