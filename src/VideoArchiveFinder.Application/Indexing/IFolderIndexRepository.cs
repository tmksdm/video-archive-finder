namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderIndexRepository
{
    Task UpsertBatchAsync(
        IReadOnlyCollection<FolderIndexUpsertItem> folders,
        CancellationToken cancellationToken = default);

    Task<int> CompleteScanAsync(
        Guid rootSourceId,
        DateTimeOffset scanStartedAtUtc,
        IReadOnlyCollection<string> protectedPaths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedFolder>> GetByRootSourceIdAsync(
        Guid rootSourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedFolder>> GetChildrenAsync(
        long parentFolderId,
        CancellationToken cancellationToken = default);
}
