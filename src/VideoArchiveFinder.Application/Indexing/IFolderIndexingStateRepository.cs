namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderIndexingStateRepository
{
    Task SaveAsync(
        FolderIndexingState state,
        CancellationToken cancellationToken = default);

    Task<FolderIndexingState?> GetAsync(
        Guid rootSourceId,
        CancellationToken cancellationToken = default);
}
