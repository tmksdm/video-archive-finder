using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.Tests.ViewModels;

/// <summary>
/// Подставная постраничная служба: выдаёт совпадения из заранее заданного
/// списка порциями фиксированного размера и позволяет приостановить первую или
/// следующую порцию, чтобы проверить отмену, устаревшие ответы и повторный
/// вход.
/// </summary>
internal sealed class StubFolderSearchPageService
    : IFolderSearchPageService
{
    private readonly Func<
        FolderSearchQuery,
        IReadOnlyList<FolderSearchResult>> _matchesFactory;

    private readonly int _pageSizeOverride;

    public StubFolderSearchPageService(
        IReadOnlyList<FolderSearchResult> matches,
        int pageSize = 0)
        : this(_ => matches, pageSize)
    {
    }

    public StubFolderSearchPageService(
        Func<
            FolderSearchQuery,
            IReadOnlyList<FolderSearchResult>> matchesFactory,
        int pageSize = 0)
    {
        _matchesFactory = matchesFactory;
        _pageSizeOverride = pageSize;
    }

    public int FirstPageCallCount { get; private set; }

    public int NextPageCallCount { get; private set; }

    public List<FolderSearchQuery> FirstPageQueries { get; } = [];

    /// <summary>
    /// Одноразовая приостановка первой порции. Параметр — токен отмены вызова:
    /// тест сам решает, наблюдать ли отмену, и тем самым отличает сценарий
    /// отмены от сценария «устаревший ответ пришёл позже».
    /// </summary>
    public Func<CancellationToken, Task>? BeforeFirstPageAsync
    {
        get;
        set;
    }

    /// <summary>Одноразовая приостановка следующей порции.</summary>
    public Func<CancellationToken, Task>? BeforeNextPageAsync
    {
        get;
        set;
    }

    public async Task<FolderSearchPage> SearchFirstPageAsync(
        FolderSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        FirstPageCallCount++;
        FirstPageQueries.Add(query);

        var gate = BeforeFirstPageAsync;
        BeforeFirstPageAsync = null;

        if (gate is not null)
        {
            await gate(cancellationToken);
        }

        var allMatches = _matchesFactory(query);
        var pageSize = EffectivePageSize(query.MaxResults);

        var matches = allMatches
            .Take(pageSize)
            .ToList();

        var session = new FolderSearchSession(
            QueryText: query.Text,
            Mode: query.Mode,
            RootSourceIds: query.RootSourceIds?
                .ToArray(),
            VideoFileRootSourceIds:
                query.VideoFileRootSourceIds?
                    .ToArray(),
            PageSize: pageSize,
            FolderMatchCount: allMatches.Count,
            VideoMatchCount: 0,
            FolderLoadedCount: matches.Count,
            VideoLoadedCount: 0,
            FolderCursor: null,
            VideoCursor: null);

        return new FolderSearchPage(
            Matches: matches,
            FolderMatchCount: allMatches.Count,
            VideoMatchCount: 0,
            FolderLoadedCount: matches.Count,
            VideoLoadedCount: 0,
            NextSession: session);
    }

    public async Task<FolderSearchPage> SearchNextPageAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default)
    {
        NextPageCallCount++;

        var gate = BeforeNextPageAsync;
        BeforeNextPageAsync = null;

        if (gate is not null)
        {
            await gate(cancellationToken);
        }

        var allMatches = _matchesFactory(
            new FolderSearchQuery(
                session.QueryText,
                session.Mode,
                session.PageSize,
                session.RootSourceIds,
                session.VideoFileRootSourceIds));

        var alreadyLoaded = session.FolderLoadedCount;

        var matches = allMatches
            .Skip(alreadyLoaded)
            .Take(session.PageSize)
            .ToList();

        var loadedCount = alreadyLoaded + matches.Count;

        var nextSession = session with
        {
            FolderLoadedCount = loadedCount
        };

        return new FolderSearchPage(
            Matches: matches,
            FolderMatchCount: allMatches.Count,
            VideoMatchCount: 0,
            FolderLoadedCount: loadedCount,
            VideoLoadedCount: 0,
            NextSession: nextSession);
    }

    private int EffectivePageSize(int requestedPageSize)
    {
        return _pageSizeOverride > 0
            ? _pageSizeOverride
            : requestedPageSize;
    }
}
