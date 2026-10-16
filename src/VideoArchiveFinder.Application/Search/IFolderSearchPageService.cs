namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Постраничный поиск по папкам и именам видеофайлов.
///
/// Каждая порция формируется запросом к SQLite, поэтому в памяти не накапливаются
/// все совпадения. Порядок выдачи и состав совпадений совпадают с
/// <see cref="IFolderSearchService"/>; отличается только способ получения
/// следующих порций.
/// </summary>
public interface IFolderSearchPageService
{
    /// <summary>
    /// Создаёт сессию поиска и возвращает первую порцию вместе с точными
    /// счётчиками совпадений.
    /// </summary>
    Task<FolderSearchPage> SearchFirstPageAsync(
        FolderSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает следующую порцию для сессии, полученной из предыдущей порции.
    /// </summary>
    Task<FolderSearchPage> SearchNextPageAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default);
}
