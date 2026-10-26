namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Одна порция выдачи вместе с точными счётчиками и состоянием для следующей
/// порции.
/// </summary>
/// <param name="Matches">
/// Совпадения этой порции: сначала папки, затем видеофайлы — в том же порядке,
/// что и у существующего поиска.
/// </param>
/// <param name="FolderMatchCount">Точное число совпадений по папкам.</param>
/// <param name="VideoMatchCount">Точное число совпадений по видеофайлам.</param>
/// <param name="FolderLoadedCount">
/// Сколько совпадений по папкам выдано с учётом этой порции.
/// </param>
/// <param name="VideoLoadedCount">
/// Сколько совпадений по видеофайлам выдано с учётом этой порции.
/// </param>
/// <param name="NextSession">
/// Состояние сессии, используемое для следующей порции и проверки версии перед
/// публикацией. Даже после последней порции вызывающая сторона освобождает его
/// через <see cref="IFolderSearchPageService.ReleaseSession"/>.
/// </param>
public sealed record FolderSearchPage(
    IReadOnlyList<FolderSearchResult> Matches,
    int FolderMatchCount,
    int VideoMatchCount,
    int FolderLoadedCount,
    int VideoLoadedCount,
    FolderSearchSession NextSession)
{
    /// <summary>Точное общее число совпадений по обоим потокам.</summary>
    public int TotalMatchCount =>
        FolderMatchCount + VideoMatchCount;

    /// <summary>Сколько совпадений выдано с учётом этой порции.</summary>
    public int LoadedMatchCount =>
        FolderLoadedCount + VideoLoadedCount;

    /// <summary>Есть ли ещё не выданные совпадения.</summary>
    public bool HasMoreResults =>
        NextSession.HasMoreResults;
}
