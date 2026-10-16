using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Infrastructure.Search;

/// <summary>
/// Точечный поиск по папкам и именам видеофайлов: одна выдача, ограниченная
/// <see cref="FolderSearchQuery.MaxResults"/>.
///
/// Реализация сводится к первой порции <see cref="IFolderSearchPageService"/>,
/// поэтому правила поиска, сортировка, приоритет папок и обработка источников
/// существуют в единственном экземпляре и не могут разойтись с постраничной
/// выдачей.
/// </summary>
public sealed class SqliteFolderSearchService
    : IFolderSearchService
{
    private readonly IFolderSearchPageService
        _folderSearchPageService;

    public SqliteFolderSearchService(
        IFolderSearchPageService folderSearchPageService)
    {
        ArgumentNullException.ThrowIfNull(folderSearchPageService);

        _folderSearchPageService = folderSearchPageService;
    }

    public async Task<IReadOnlyList<FolderSearchResult>>
        SearchAsync(
            FolderSearchQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Проверка запроса, нормализация, область источников, отмена и ведение
        // журнала выполняются постраничным сервисом: точечный поиск обязан
        // вести себя так же, как первая порция постраничного.
        var page = await _folderSearchPageService
            .SearchFirstPageAsync(query, cancellationToken)
            .ConfigureAwait(false);

        return page.Matches;
    }
}
