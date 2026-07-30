namespace VideoArchiveFinder.Application.Search;

public interface IFolderSearchContextProvider
{
    Task<IReadOnlyList<FolderSearchResult>>
        GetContextFoldersAsync(
            IReadOnlyCollection<FolderSearchResult> matches,
            CancellationToken cancellationToken = default);
}
