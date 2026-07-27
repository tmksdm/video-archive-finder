namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderIndexRepository
{
    Task UpsertBatchAsync(
        IReadOnlyCollection<FolderIndexUpsertItem> folders,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedFolder>> GetByRootSourceIdAsync(
        Guid rootSourceId,
        CancellationToken cancellationToken = default);
}
