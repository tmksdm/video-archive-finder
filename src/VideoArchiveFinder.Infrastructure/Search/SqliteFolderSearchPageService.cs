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
///
/// Сессия поиска владеет одним read-only соединением. На каждой порции в одной
/// короткой транзакции читаются <c>PRAGMA data_version</c> и сама порция; если
/// версия отличается от снятой при создании сессии, порция не читается и
/// подгрузка останавливается (<see cref="FolderSearchIndexChangedException"/>).
/// </summary>
public sealed class SqliteFolderSearchPageService
    : IFolderSearchPageService, IDisposable
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ISearchStemService
        _searchStemService;

    private readonly ILogger<SqliteFolderSearchPageService>
        _logger;

    private readonly object _leaseGate = new();

    private readonly Dictionary<Guid, SessionLease>
        _leases = [];

    private bool _disposed;

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

        ThrowIfDisposed();

        var normalizedQuery =
            _textNormalizationService.Normalize(query.Text);

        // null означает «без ограничения по источникам» и обязан остаться null:
        // прежний поиск в этом случае искал по всем папкам индекса. Пустая
        // коллекция, наоборот, означает отсутствие области поиска.
        var rootSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray();

        var videoFileRootSourceIds =
            query.VideoFileRootSourceIds?
                .Distinct()
                .ToArray();

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
            (rootSourceIds is { Length: 0 } &&
             videoFileRootSourceIds is not { Length: > 0 }))
        {
            // Пустой выдаче соединение не нужно: SessionId остаётся
            // Guid.Empty, и ValidateSessionAsync отличает такую сессию от
            // освобождённой сессии, у которой соединение было.
            return CreateEmptyPage(session);
        }

        session = session with
        {
            SessionId = Guid.NewGuid()
        };

        SessionLease? lease = null;
        var keepLease = false;

        try
        {
            var connection = CreateConnection();

            try
            {
                await connection
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await ConfigureConnectionAsync(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

                // S0 снимается вне транзакции, как в разделе 4.6: сверка
                // каждой порции выполняется уже внутри её транзакции.
                var initialDataVersion =
                    await ReadDataVersionAsync(
                            connection,
                            cancellationToken)
                        .ConfigureAwait(false);

                var candidate = new SessionLease(
                    session.SessionId,
                    connection,
                    initialDataVersion);

                if (!TryRegisterLease(candidate))
                {
                    candidate.RequestRelease();

                    throw new ObjectDisposedException(
                        nameof(SqliteFolderSearchPageService));
                }

                lease = candidate;
            }
            catch
            {
                connection.Dispose();

                throw;
            }

            var page = await ExecuteVersionedReadAsync(
                    lease,
                    cancellationToken,
                    connectionInLease => ReadFirstPageAsync(
                        connectionInLease,
                        session,
                        normalizedQuery,
                        cancellationToken))
                .ConfigureAwait(false);

            // Сессия живёт до ReleaseSession/ValidateSessionAsync: даже
            // последняя страница не освобождает соединение автоматически.
            keepLease = true;

            return page;
        }
        catch (FolderSearchIndexChangedException)
        {
            throw;
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
        finally
        {
            if (lease is not null && !keepLease)
            {
                ReleaseLease(lease);
            }
        }
    }

    public async Task<FolderSearchPage> SearchNextPageAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfDisposed();

        if (!session.HasMoreResults)
        {
            return CreateEmptyPage(session);
        }

        var lease = FindLease(session.SessionId);

        if (lease is null)
        {
            throw new ObjectDisposedException(
                nameof(FolderSearchSession),
                "Сессия поиска уже освобождена.");
        }

        var normalizedQuery =
            _textNormalizationService.Normalize(session.QueryText);

        try
        {
            return await ExecuteVersionedReadAsync(
                    lease,
                    cancellationToken,
                    connection => LoadPageAsync(
                        connection,
                        session,
                        normalizedQuery,
                        cancellationToken))
                .ConfigureAwait(false);
        }
        catch (FolderSearchIndexChangedException)
        {
            throw;
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

    public async Task ValidateSessionAsync(
        FolderSearchSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfDisposed();

        if (session.SessionId == Guid.Empty)
        {
            // Пустая выдача не создаёт соединение: проверять нечего.
            return;
        }

        var lease = FindLease(session.SessionId);

        if (lease is null || lease.IsReleaseRequestedOrReleased)
        {
            throw new ObjectDisposedException(
                nameof(FolderSearchSession),
                "Сессия поиска уже освобождена или неизвестна.");
        }

        try
        {
            // Проверка версии в отдельной короткой транзакции на том же
            // соединении: так изменение, закоммиченное после последней порции,
            // обнаруживается до публикации результатов.
            await ExecuteVersionedReadAsync(
                    lease,
                    cancellationToken,
                    static _ => Task.FromResult(true))
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Освобождение обогнало проверку: сессия больше не действительна.
            throw;
        }
        catch (FolderSearchIndexChangedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Search session validation was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not validate search session {SessionId}.",
                session.SessionId);

            throw;
        }
    }

    public void ReleaseSession(FolderSearchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.SessionId == Guid.Empty)
        {
            return;
        }

        SessionLease? lease = null;

        lock (_leaseGate)
        {
            if (_leases.TryGetValue(
                    session.SessionId,
                    out var registered))
            {
                lease = registered;

                _leases.Remove(session.SessionId);
            }
        }

        lease?.RequestRelease();
    }

    public void Dispose()
    {
        SessionLease[] leases;

        lock (_leaseGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            leases = _leases.Values.ToArray();

            _leases.Clear();
        }

        foreach (var lease in leases)
        {
            lease.RequestRelease();
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

    private async Task<FolderSearchPage> ReadFirstPageAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var folderMatchCount =
            session.RootSourceIds is { Count: 0 }
                ? 0
                : await CountFoldersAsync(
                        connection,
                        session,
                        normalizedQuery,
                        cancellationToken)
                    .ConfigureAwait(false);

        var videoMatchCount =
            session.VideoFileRootSourceIds is not { Count: > 0 }
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

    /// <summary>
    /// Выполняет чтение в короткой транзакции: отложенный <c>BEGIN</c>, затем
    /// <c>PRAGMA data_version</c> на том же соединении, сверка с S0 и только
    /// после неё — сама порция. Любое изменение, закоммиченное другим
    /// соединением, останавливает чтение; тройка-диагностика не участвует в
    /// решении.
    /// </summary>
    private async Task<T> ExecuteVersionedReadAsync<T>(
        SessionLease lease,
        CancellationToken cancellationToken,
        Func<SqliteConnection, Task<T>> read)
    {
        await lease.Gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        SqliteTransaction? transaction = null;

        try
        {
            lease.EnterOperation();

            transaction = lease.Connection.BeginTransaction(deferred: true);

            var currentDataVersion =
                await ReadDataVersionAsync(
                        lease.Connection,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (currentDataVersion != lease.InitialDataVersion)
            {
                TryRollback(transaction);
                transaction.Dispose();
                transaction = null;

                lease.MarkIndexChanged();

                _logger.LogWarning(
                    "Search index changed during session {SessionId}: " +
                    "data_version was {InitialDataVersion}, now {CurrentDataVersion}. " +
                    "Further paging is stopped.",
                    lease.SessionId,
                    lease.InitialDataVersion,
                    currentDataVersion);

                throw new FolderSearchIndexChangedException(
                    lease.SessionId);
            }

            var result = await read(lease.Connection)
                .ConfigureAwait(false);

            transaction.Commit();
            transaction.Dispose();
            transaction = null;

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                TryRollback(transaction);
                transaction.Dispose();
            }

            lease.ExitOperation();
        }
    }

    private async Task<FolderSearchPage> LoadPageAsync(
        SqliteConnection connection,
        FolderSearchSession session,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var folderTake = session.RootSourceIds is { Count: 0 }
            ? 0
            : Math.Min(
                session.PageSize,
                session.FolderMatchCount - session.FolderLoadedCount);

        var videoTake = session.VideoFileRootSourceIds is not { Count: > 0 }
            ? 0
            : Math.Min(
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

        var sourceFilter = FolderSearchSql.BuildRootSourceIdFilter(
            "RootSourceId",
            session.RootSourceIds,
            "rootSourceId");

        command.CommandText = sourceFilter.Length == 0
            ? $"""
              SELECT COUNT(*)
              FROM Folders
              WHERE {searchCondition};
              """
            : $"""
              SELECT COUNT(*)
              FROM Folders
              WHERE {searchCondition}
                AND {sourceFilter};
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
        var sourceFilter = FolderSearchSql.BuildRootSourceIdFilter(
            "v.RootSourceId",
            session.VideoFileRootSourceIds,
            "videoRootSourceId");

        command.CommandText = sourceFilter.Length == 0
            ? $"""
              SELECT COUNT(*)
              FROM VideoFiles v
              INNER JOIN Folders f
                  ON f.RootSourceId = v.RootSourceId
                 AND f.FullPath = v.FolderFullPath
              WHERE {searchCondition}
                AND v.IsAvailable = 1;
              """
            : $"""
              SELECT COUNT(*)
              FROM VideoFiles v
              INNER JOIN Folders f
                  ON f.RootSourceId = v.RootSourceId
                 AND f.FullPath = v.FolderFullPath
              WHERE {searchCondition}
                AND v.IsAvailable = 1
                AND {sourceFilter};
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

                // Сессия живёт долго, а после ReleaseSession её native handle
                // обязан закрыться сразу: иначе Windows не даст заменить или
                // удалить файл индекса, а глобальный ClearAllPools старых
                // тестов мог бы влиять на новые сессии.
                Pooling = false
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

    private static async Task<long> ReadDataVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA data_version;";

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt64(result);
    }

    private static void TryRollback(
        SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (InvalidOperationException)
        {
            // Транзакция уже завершена.
        }
        catch (SqliteException)
        {
            // Соединение уже не позволяет откатить транзакцию.
        }
    }

    private bool TryRegisterLease(SessionLease lease)
    {
        lock (_leaseGate)
        {
            if (_disposed)
            {
                return false;
            }

            _leases.Add(lease.SessionId, lease);

            return true;
        }
    }

    private SessionLease? FindLease(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        lock (_leaseGate)
        {
            return _leases.TryGetValue(
                    sessionId,
                    out var lease)
                ? lease
                : null;
        }
    }

    private void ReleaseLease(SessionLease lease)
    {
        lock (_leaseGate)
        {
            if (_leases.TryGetValue(
                    lease.SessionId,
                    out var registered) &&
                ReferenceEquals(registered, lease))
            {
                _leases.Remove(lease.SessionId);
            }
        }

        lease.RequestRelease();
    }

    private void ThrowIfDisposed()
    {
        lock (_leaseGate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(SqliteFolderSearchPageService));
            }
        }
    }

    private sealed record StreamPage(
        IReadOnlyList<FolderSearchResult> Matches,
        FolderSearchCursor? NextCursor);

    /// <summary>
    /// Владелец read-only соединения одной сессии. Операции и освобождение
    /// сериализуются семафором; освобождение, пришедшее во время операции, не
    /// закрывает занятое соединение, а откладывается до её завершения.
    /// </summary>
    private sealed class SessionLease
    {
        private readonly object _stateGate = new();

        private readonly SqliteConnection _connection;

        private bool _operationActive;
        private bool _releaseRequested;
        private bool _released;
        private bool _indexChanged;

        public SessionLease(
            Guid sessionId,
            SqliteConnection connection,
            long initialDataVersion)
        {
            SessionId = sessionId;
            _connection = connection;
            InitialDataVersion = initialDataVersion;
        }

        public Guid SessionId { get; }

        public long InitialDataVersion { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public SqliteConnection Connection => _connection;

        public bool IsReleaseRequestedOrReleased
        {
            get
            {
                lock (_stateGate)
                {
                    return _releaseRequested || _released;
                }
            }
        }

        public void EnterOperation()
        {
            lock (_stateGate)
            {
                if (_released || _releaseRequested)
                {
                    throw new ObjectDisposedException(
                        nameof(FolderSearchSession),
                        "Сессия поиска уже освобождена.");
                }

                if (_indexChanged)
                {
                    throw new FolderSearchIndexChangedException(
                        SessionId);
                }

                _operationActive = true;
            }
        }

        public void ExitOperation()
        {
            bool close;

            lock (_stateGate)
            {
                _operationActive = false;
                close = _releaseRequested && !_released;

                if (close)
                {
                    _released = true;
                }
            }

            if (close)
            {
                _connection.Dispose();
            }

            Gate.Release();
        }

        public void MarkIndexChanged()
        {
            lock (_stateGate)
            {
                _indexChanged = true;
            }
        }

        public void RequestRelease()
        {
            bool close;

            lock (_stateGate)
            {
                _releaseRequested = true;
                close = !_operationActive && !_released;

                if (close)
                {
                    _released = true;
                }
            }

            if (close)
            {
                _connection.Dispose();
            }
        }
    }
}
