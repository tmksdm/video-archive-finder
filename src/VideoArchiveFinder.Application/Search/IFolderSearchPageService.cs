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

    /// <summary>
    /// Закрывает ресурсы сессии, если они ещё открыты. Повторный вызов
    /// безопасен. Освобождение не прерывает уже выполняющуюся операцию: если
    /// соединение занято, оно закрывается сразу после её завершения.
    /// </summary>
    void ReleaseSession(FolderSearchSession session);

    /// <summary>
    /// Проверяет на том же соединении, что и последняя порция, что индекс не
    /// изменился после её чтения. Несовместимая версия завершается
    /// <see cref="FolderSearchIndexChangedException"/>; освобождённая или
    /// неизвестная сессия — <see cref="ObjectDisposedException"/>. Сессия
    /// пустой выдачи (<see cref="FolderSearchSession.SessionId"/> равен
    /// <see cref="Guid.Empty"/>) ресурсов не держит и проверяется без ошибки.
    ///
    /// Вызывается после построения представления и перед публикацией порции;
    /// освобождение сессии остаётся за вызывающей стороной.
    /// </summary>
    Task ValidateSessionAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default);
}
