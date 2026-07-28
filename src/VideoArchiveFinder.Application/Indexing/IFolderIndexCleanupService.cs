namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderIndexCleanupService
{
    Task DeleteByRootSourceIdsAsync(
        IReadOnlyCollection<Guid> rootSourceIds,
        CancellationToken cancellationToken = default);
}
