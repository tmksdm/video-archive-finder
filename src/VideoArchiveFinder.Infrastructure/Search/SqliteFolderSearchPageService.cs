using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Infrastructure.Search;

/// <summary>
/// Постраничный поиск по папкам и именам видеофайлов.
///
/// Каждая порция читается из индекса одним запросом с продолжением строго после
/// курсора: <c>(Priority, length(Name), Name COLLATE NOCASE, Id) &gt; (…)</c>.
/// Курсор — четыре значения на поток, поэтому в памяти не накапливаются ни все
/// совпадения, ни их ключи сортировки. Порядок выдачи и состав совпадений
/// совпадают с <see cref="SqliteFolderSearchService"/>: сначала папки, затем
/// видеофайлы, общий бюджет порции делится между потоками.
/// </summary>
public sealed class SqliteFolderSearchPageService
    : IFolderSearchPageService
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ISearchStemService
        _searchStemService;

    private readonly ILogger<SqliteFolderSearchPageService>
        _logger;

    public SqliteFolderSearchPageService(
        IndexDatabasePathProvider databasePathProvider,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        ILogger<SqliteFolderSearchPageService> logger)
    {
        _databasePathProvider = databasePathProvider;
        _textNormalizationService = textNormalizationService;
        _searchStemService = searchStemService;
        _logger = logger;
    }

    public async Task<FolderSearchPage> SearchFirstPageAsync(
        FolderSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        FolderSearchSql.ValidateQuery(query);

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedQuery =
            _textNormalizationService.Normalize(query.Text);

        var rootSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray() ?? [];

        var videoFileRootSourceIds =
            query.VideoFileRootSourceIds?
                .Distinct()
                .ToArray() ?? [];

        var session = new FolderSearchSession(
            QueryText: query.Text,
            Mode: query.Mode,
            RootSourceIds: rootSourceIds,
            VideoFileRootSourceIds: videoFileRootSourceIds,
            PageSize: query.MaxResults,
            FolderMatchCount: 0,
            VideoMatchCount: 0,
            FolderLoadedCount: 0,
            VideoLoadedCount: 0,
            FolderCursor: null,
            VideoCursor: null);

        if (normalizedQuery.Length == 0 ||
            (rootSourceIds.Length == 0 &&
             videoFileRootSourceIds.Length == 0))
        {
            return CreateEmptyPage(session);
        }

        try
        {
            await using var connection = CreateConnection();

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            var folderMatchCount =
                rootSourceIds.Length == 0
                    ? 0
                    : await CountFoldersAsync(
                            connection,
                            session,
                            normalizedQuery,
                            cancellationToken)
                        .ConfigureAwait(false);

            var videoMatchCount =
                videoFileRootSourceIds.Length == 0
                    ? 0
                    : await CountVideoFilesAsync(
                            connection,
                            session,
                            normalizedQuery,
                            cancellationToken)
                        .ConfigureAwait(false);

            var countedSession = session with
            {
                FolderMatchCount = folderMatchCount,
                VideoMatchCount = videoMatchCount
            };

            return await LoadPageAsync(
                    connection,
                    countedSession,
                    normalizedQuery,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Paged folder search was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Paged folder search failed in {SearchMode} mode.",
                query.Mode);

            throw;
        }
    }

    public async Task<FolderSearchPage> SearchNextPageAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        cancellationToken.ThrowIfCancellationRequested();

        if (!session.HasMoreResults)
        {
            return CreateEmptyPage(session);
        }

        var normalizedQuery =
            _textNormalizationService.Normalize(session.QueryText);

        try
        {
            await using var connection = CreateConnection();

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            return await LoadPageAsync(
                    connection,
                    session,
                    normalizedQuery,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Paged folder search was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Paged folder search failed in {SearchMode} mode.",
                session.Mode);

            throw;
        }
    }

    private static FolderSearchPage CreateEmptyPage(
        FolderSearchSession session)
    {
        return new FolderSearchPage(
            Matches: [],
            FolderMatchCount: session.FolderMatchCount,
            VideoMatchCount: session.VideoMatchCount,
            FolderLoadedCount: session.FolderLoadedCount,
            VideoLoadedCount: session.VideoLoadedCount,
            NextSession: session);
    }

    private async Task<FolderSearchPage> LoadPageAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var folderTake = Math.Min(
            session.PageSize,
            session.FolderMatchCount - session.FolderLoadedCount);

        var videoTake = Math.Min(
            session.PageSize - folderTake,
            session.VideoMatchCount - session.VideoLoadedCount);

        var matches = new List<FolderSearchResult>(
            folderTake + videoTake);

        var folderCursor = session.FolderCursor;
        var videoCursor = session.VideoCursor;
        var folderLoadedCount = session.FolderLoadedCount;
        var videoLoadedCount = session.VideoLoadedCount;

        if (folderTake > 0)
        {
            var page = await ReadFolderPageAsync(
                    connection,
                    session,
                    normalizedQuery,
                    folderTake,
                    folderCursor,
                    cancellationToken)
                .ConfigureAwait(false);

            matches.AddRange(page.Matches);
            folderLoadedCount += page.Matches.Count;
            folderCursor = page.NextCursor ?? folderCursor;
        }

        if (videoTake > 0)
        {
            var page = await ReadVideoPageAsync(
                    connection,
                    session,
                    normalizedQuery,
                    videoTake,
                    videoCursor,
                    cancellationToken)
                .ConfigureAwait(false);

            matches.AddRange(page.Matches);
            videoLoadedCount += page.Matches.Count;
            videoCursor = page.NextCursor ?? videoCursor;
        }

        var nextSession = session with
        {
            FolderLoadedCount = folderLoadedCount,
            VideoLoadedCount = videoLoadedCount,
            FolderCursor = folderCursor,
            VideoCursor = videoCursor
        };

        return new FolderSearchPage(
            Matches: matches,
            FolderMatchCount: nextSession.FolderMatchCount,
            VideoMatchCount: nextSession.VideoMatchCount,
            FolderLoadedCount: folderLoadedCount,
            VideoLoadedCount: videoLoadedCount,
            NextSession: nextSession);
    }

    private async Task<int> CountFoldersAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildFolderSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                CreateQuery(session, session.Mode));

        command.CommandText =
            $"""
            SELECT COUNT(*)
            FROM Folders
            WHERE {searchCondition}
              AND {FolderSearchSql.BuildRootSourceIdFilter(
                    "RootSourceId",
                    session.RootSourceIds,
                    "rootSourceId")};
            """;

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizedQuery);

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            session.RootSourceIds);

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(result);
    }

    private async Task<int> CountVideoFilesAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildVideoSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                CreateQuery(session, session.Mode));

        // Соединение с папками входит в счётчик намеренно: постраничная выдача
        // тоже соединяет таблицы, поэтому счётчик обязан считать ровно те
        // строки, которые достижимы при листании.
        command.CommandText =
            $"""
            SELECT COUNT(*)
            FROM VideoFiles v
            INNER JOIN Folders f
                ON f.RootSourceId = v.RootSourceId
               AND f.FullPath = v.FolderFullPath
            WHERE {searchCondition}
              AND v.IsAvailable = 1
              AND {FolderSearchSql.BuildRootSourceIdFilter(
                    "v.RootSourceId",
                    session.VideoFileRootSourceIds,
                    "videoRootSourceId")};
            """;

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizedQuery);

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            session.VideoFileRootSourceIds,
            "videoRootSourceId");

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(result);
    }

    private async Task<StreamPage> ReadFolderPageAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        int take,
        FolderSearchCursor? cursor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildFolderSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                CreateQuery(session, session.Mode));

        command.CommandText = FolderSearchSql.BuildFolderPageCommand(
            searchCondition,
            session.RootSourceIds);

        AddPageParameters(
            command,
            normalizedQuery,
            take,
            cursor);

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            session.RootSourceIds);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var matches = new List<FolderSearchResult>(take);
        FolderSearchCursor? nextCursor = null;

        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            matches.Add(FolderSearchSql.ReadResult(reader));

            nextCursor = FolderSearchSql.ReadPageCursor(
                reader,
                FolderSearchSql.FolderPagePriorityColumn,
                FolderSearchSql.FolderPageNameLengthColumn,
                FolderSearchSql.FolderPageRowKeyColumn);
        }

        return new StreamPage(matches, nextCursor);
    }

    private async Task<StreamPage> ReadVideoPageAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        int take,
        FolderSearchCursor? cursor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildVideoSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                CreateQuery(session, session.Mode));

        command.CommandText = FolderSearchSql.BuildVideoPageCommand(
            searchCondition,
            session.VideoFileRootSourceIds);

        AddPageParameters(
            command,
            normalizedQuery,
            take,
            cursor);

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            session.VideoFileRootSourceIds,
            "videoRootSourceId");

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var matches = new List<FolderSearchResult>(take);
        FolderSearchCursor? nextCursor = null;

        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            matches.Add(FolderSearchSql.ReadVideoResult(reader));

            nextCursor = FolderSearchSql.ReadPageCursor(
                reader,
                FolderSearchSql.VideoPagePriorityColumn,
                FolderSearchSql.VideoPageNameLengthColumn,
                FolderSearchSql.VideoPageRowKeyColumn);
        }

        return new StreamPage(matches, nextCursor);
    }

    private static void AddPageParameters(
        SqliteCommand command,
        string normalizedQuery,
        int take,
        FolderSearchCursor? cursor)
    {
        var effectiveCursor =
            cursor ?? FolderSearchSql.InitialCursor;

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizedQuery);

        command.Parameters.AddWithValue("$pageSize", take);

        command.Parameters.AddWithValue(
            "$cursorPriority",
            effectiveCursor.Priority);

        command.Parameters.AddWithValue(
            "$cursorLength",
            effectiveCursor.Length);

        command.Parameters.AddWithValue(
            "$cursorName",
            effectiveCursor.Name);

        command.Parameters.AddWithValue(
            "$cursorId",
            effectiveCursor.Id);
    }

    /// <summary>
    /// Запрос с параметрами режима и текста сессии. Условия поиска строятся тем
    /// же помощником, что и для точечного поиска, поэтому семантика «умного» и
    /// точного режимов совпадает.
    /// </summary>
    private static FolderSearchQuery CreateQuery(
        FolderSearchSession session,
        FolderSearchMode mode)
    {
        return new FolderSearchQuery(
            session.QueryText,
            mode,
            session.PageSize,
            session.RootSourceIds,
            session.VideoFileRootSourceIds);
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePathProvider.GetDatabasePath(),

                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record StreamPage(
        IReadOnlyList<FolderSearchResult> Matches,
        FolderSearchCursor? NextCursor);
}
