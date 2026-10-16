namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Состояние постраничной выдачи: неизменяемые параметры запроса, счётчики и
/// позиции обоих потоков.
///
/// Сессия не содержит совпадений — только курсоры и числа, поэтому память не
/// зависит от количества найденных строк. Новое состояние возвращается вместе с
/// каждой порцией, прежнее остаётся неизменным.
/// </summary>
/// <param name="RootSourceIds">
/// Источники для поиска по папкам. <c>null</c> означает «без ограничения по
/// источникам» — так же, как в <see cref="FolderSearchQuery.RootSourceIds"/>;
/// пустая коллекция означает, что поиск по папкам не выполняется.
/// </param>
/// <param name="VideoFileRootSourceIds">
/// Источники для поиска по именам видеофайлов. <c>null</c> или пустая коллекция
/// означают, что поиск по видеофайлам не выполняется.
/// </param>
public sealed record FolderSearchSession(
    string QueryText,
    FolderSearchMode Mode,
    IReadOnlyList<Guid>? RootSourceIds,
    IReadOnlyList<Guid>? VideoFileRootSourceIds,
    int PageSize,
    int FolderMatchCount,
    int VideoMatchCount,
    int FolderLoadedCount,
    int VideoLoadedCount,
    FolderSearchCursor? FolderCursor,
    FolderSearchCursor? VideoCursor)
{
    /// <summary>Точное общее число совпадений по папкам и видеофайлам.</summary>
    public int TotalMatchCount =>
        FolderMatchCount + VideoMatchCount;

    /// <summary>Сколько совпадений уже выдано пользователю.</summary>
    public int LoadedMatchCount =>
        FolderLoadedCount + VideoLoadedCount;

    /// <summary>Есть ли ещё не выданные совпадения.</summary>
    public bool HasMoreResults =>
        FolderLoadedCount < FolderMatchCount ||
        VideoLoadedCount < VideoMatchCount;
}
