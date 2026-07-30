namespace VideoArchiveFinder.Application.Search;

public interface IFolderSearchService
{
    Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        FolderSearchQuery query,
        CancellationToken cancellationToken = default);
}
