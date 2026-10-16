namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Состояние постраничной выдачи: неизменяемые параметры запроса, счётчики и
/// позиции обоих потоков.
///
/// Сессия не содержит совпадений — только курсоры и числа, поэтому память не
/// зависит от количества найденных строк. Новое состояние возвращается вместе с
/// каждой порцией, прежнее остаётся неизменным.
/// </summary>
public sealed record FolderSearchSession(
    string QueryText,
    FolderSearchMode Mode,
    IReadOnlyList<Guid> RootSourceIds,
    IReadOnlyList<Guid> VideoFileRootSourceIds,
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
