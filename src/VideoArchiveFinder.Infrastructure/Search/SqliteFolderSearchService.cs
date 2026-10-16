using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Infrastructure.Search;

/// <summary>
/// Точечный поиск по папкам и именам видеофайлов: одна выдача, ограниченная
/// <see cref="FolderSearchQuery.MaxResults"/>.
///
/// Условия поиска, порядок сортировки и чтение результатов берутся из
/// <see cref="FolderSearchSql"/>, поэтому семантика совпадает с постраничным
/// поиском <see cref="SqliteFolderSearchPageService"/>.
/// </summary>
public sealed class SqliteFolderSearchService
    : IFolderSearchService
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ISearchStemService
        _searchStemService;

    private readonly ILogger<SqliteFolderSearchService>
        _logger;

    public SqliteFolderSearchService(
        IndexDatabasePathProvider databasePathProvider,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        ILogger<SqliteFolderSearchService> logger)
    {
        _databasePathProvider = databasePathProvider;
        _textNormalizationService = textNormalizationService;
        _searchStemService = searchStemService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FolderSearchResult>>
        SearchAsync(
            FolderSearchQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        FolderSearchSql.ValidateQuery(query);

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedQuery =
            _textNormalizationService.Normalize(query.Text);

        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var rootSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray();

        var videoFileRootSourceIds = query.VideoFileRootSourceIds?
            .Distinct()
            .ToArray();

        if (rootSourceIds is { Length: 0 } &&
            videoFileRootSourceIds is not { Length: > 0 })
        {
            return [];
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

            var folderResults = rootSourceIds is { Length: 0 }
                ? []
                : await SearchFoldersAsync(
                        connection,
                        query,
                        normalizedQuery,
                        rootSourceIds,
                        cancellationToken)
                    .ConfigureAwait(false);

            var videoResults = videoFileRootSourceIds is { Length: > 0 }
                ? await SearchVideoFilesAsync(
                        connection,
                        query,
                        normalizedQuery,
                        videoFileRootSourceIds,
                        cancellationToken)
                    .ConfigureAwait(false)
                : [];

            return MergeResults(
                folderResults,
                videoResults,
                query.MaxResults);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Folder search was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Folder search failed in {SearchMode} mode.",
                query.Mode);

            throw;
        }
    }

    private async Task<IReadOnlyList<FolderSearchResult>>
        SearchFoldersAsync(
            SqliteConnection connection,
            FolderSearchQuery query,
            string normalizedQuery,
            IReadOnlyList<Guid>? rootSourceIds,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            rootSourceIds);

        var searchCondition =
            FolderSearchSql.BuildFolderSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                query);

        command.CommandText =
            FolderSearchSql.BuildFolderSelectCommand(
                searchCondition,
                rootSourceIds);

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizedQuery);
        command.Parameters.AddWithValue(
            "$maxResults",
            query.MaxResults);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<FolderSearchResult>();

        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            results.Add(FolderSearchSql.ReadResult(reader));
        }

        return results;
    }

    private async Task<IReadOnlyList<FolderSearchResult>>
        SearchVideoFilesAsync(
            SqliteConnection connection,
            FolderSearchQuery query,
            string normalizedQuery,
            IReadOnlyList<Guid> rootSourceIds,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            rootSourceIds,
            "videoRootSourceId");

        var searchCondition =
            FolderSearchSql.BuildVideoSearchCondition(
                command,
                _textNormalizationService,
                _searchStemService,
                query);

        command.CommandText =
            FolderSearchSql.BuildVideoSelectCommand(
                searchCondition,
                rootSourceIds);

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizedQuery);
        command.Parameters.AddWithValue(
            "$maxResults",
            query.MaxResults);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<FolderSearchResult>();

        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            results.Add(
                FolderSearchSql.ReadVideoResult(reader));
        }

        return results;
    }

    private static IReadOnlyList<FolderSearchResult> MergeResults(
        IEnumerable<FolderSearchResult> folderResults,
        IEnumerable<FolderSearchResult> videoResults,
        int maxResults)
    {
        var results = new List<FolderSearchResult>();

        foreach (var group in folderResults
            .Concat(videoResults)
            .GroupBy(result => result.Id))
        {
            results.Add(group.First());

            if (results.Count == maxResults)
            {
                break;
            }
        }

        return results;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePathProvider.GetDatabasePath(),

                Mode = SqliteOpenMode.ReadWriteCreate,
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
}
