using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.Tests.ViewModels;

/// <summary>
/// Подставной поставщик контекстных папок: возвращает заданный набор папок и
/// запоминает, для какого набора совпадений его вызывали.
/// </summary>
internal sealed class StubFolderSearchContextProvider
    : IFolderSearchContextProvider
{
    private readonly IReadOnlyList<FolderSearchResult>
        _contextFolders;

    public StubFolderSearchContextProvider(
        params FolderSearchResult[] contextFolders)
    {
        _contextFolders = contextFolders;
    }

    public int CallCount { get; private set; }

    public IReadOnlyList<long> LastMatchIds { get; private set; } = [];

    public Task<IReadOnlyList<FolderSearchResult>>
        GetContextFoldersAsync(
            IReadOnlyCollection<FolderSearchResult> matches,
            CancellationToken cancellationToken = default)
    {
        CallCount++;

        LastMatchIds = matches
            .Select(match => match.Id)
            .ToArray();

        return Task.FromResult(_contextFolders);
    }
}
