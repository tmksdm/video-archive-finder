using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Infrastructure.Search;

public sealed class SqliteFolderSearchService
    : IFolderSearchService
{
    private const int MinimumSmartPrefixLength = 3;
    private const int MaximumAllowedResults = 1000;

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

        ValidateQuery(query);

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

    private static string BuildExactSearchCommand(
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        return BuildSelectCommand(
            "instr(NormalizedName, $normalizedQuery) > 0",
            rootSourceIds);
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

        AddRootSourceIdParameters(command, rootSourceIds);

        command.CommandText = query.Mode switch
        {
            FolderSearchMode.Exact =>
                BuildExactSearchCommand(rootSourceIds),
            FolderSearchMode.Smart =>
                BuildSmartSearchCommand(
                    command,
                    query.Text,
                    rootSourceIds),
            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.")
        };

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
            results.Add(ReadResult(reader));
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

        AddRootSourceIdParameters(
            command,
            rootSourceIds,
            "videoRootSourceId");

        var searchCondition = query.Mode switch
        {
            FolderSearchMode.Exact =>
                "instr(v.NormalizedName, $normalizedQuery) > 0",
            FolderSearchMode.Smart =>
                BuildVideoSmartSearchCondition(command, query.Text),
            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.")
        };

        command.CommandText =
            $$"""
            SELECT
                -v.Id,
                v.FullPath,
                v.Name,
                v.NormalizedName,
                f.Id,
                f.RootSourceId,
                v.IsAvailable,
                0,
                0,
                f.FullPath
            FROM VideoFiles v
            INNER JOIN Folders f
                ON f.RootSourceId = v.RootSourceId
               AND f.FullPath = v.FolderFullPath
            WHERE {{searchCondition}}
              AND v.IsAvailable = 1
              AND v.RootSourceId IN ({{string.Join(", ",
                    Enumerable.Range(0, rootSourceIds.Count)
                        .Select(index => $"$videoRootSourceId{index}"))}})
            ORDER BY
                CASE
                    WHEN v.NormalizedName = $normalizedQuery THEN 0
                    WHEN instr(v.NormalizedName, $normalizedQuery) = 1 THEN 1
                    WHEN instr(v.NormalizedName, $normalizedQuery) > 0 THEN 2
                    ELSE 3
                END,
                length(v.Name),
                v.Name COLLATE NOCASE,
                v.Id
            LIMIT $maxResults;
            """;

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
                ReadResult(reader) with
                {
                    IsVideoFile = true,
                    NavigationFolderId = reader.GetInt64(4),
                    NavigationFolderFullPath = reader.GetString(9)
                });
        }

        return results;
    }

    private string BuildVideoSmartSearchCondition(
        SqliteCommand command,
        string queryText)
    {
        var tokens = _textNormalizationService
            .Tokenize(queryText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tokens.Length == 0)
        {
            return "instr(v.NormalizedName, $normalizedQuery) > 0";
        }

        var conditions = new List<string>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var parameterName = $"$videoToken{index}";

            command.Parameters.AddWithValue(parameterName, token);

            if (token.Length < MinimumSmartPrefixLength)
            {
                conditions.Add(
                    $"instr(v.NormalizedName, {parameterName}) > 0");
                continue;
            }

            var stem = _searchStemService.GetStem(token);
            var stemParameterName = $"$videoStem{index}";

            command.Parameters.AddWithValue(
                stemParameterName,
                stem);

            conditions.Add(
                $"(instr(v.NormalizedName, {parameterName}) > 0 " +
                $"OR instr(v.NormalizedName, {stemParameterName}) > 0)");
        }

        return string.Join(
            Environment.NewLine + " AND ",
            conditions);
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

    private string BuildSmartSearchCommand(
        SqliteCommand command,
        string queryText,
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        var tokens = _textNormalizationService
            .Tokenize(queryText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tokens.Length == 0)
        {
            return BuildExactSearchCommand(rootSourceIds);
        }

        var conditions = new List<string>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var tokenParameterName = $"$token{index}";

            command.Parameters.AddWithValue(
                tokenParameterName,
                token);

            if (token.Length < MinimumSmartPrefixLength)
            {
                conditions.Add(
                    $"""
                    (
                        NormalizedName = {tokenParameterName}
                        OR instr(
                            ' ' || SearchTokens || ' ',
                            ' ' || {tokenParameterName} || ' ') > 0
                    )
                    """);

                continue;
            }

            var stem = _searchStemService.GetStem(token);
            var stemParameterName = $"$stem{index}";

            command.Parameters.AddWithValue(
                stemParameterName,
                stem);

            conditions.Add(
                $"""
                (
                    instr(
                        NormalizedName,
                        {tokenParameterName}) > 0

                    OR instr(
                        ' ' || SearchTokens,
                        ' ' || {tokenParameterName}) > 0

                    OR instr(
                        ' ' || SearchStems,
                        ' ' || {stemParameterName}) > 0
                )
                """);
        }

        return BuildSelectCommand(
            string.Join(
                Environment.NewLine + " AND ",
                conditions),
            rootSourceIds);
    }

    private static string BuildSelectCommand(
        string searchCondition,
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        var commandText = new StringBuilder();

        commandText.AppendLine(
            """
            SELECT
                Id,
                FullPath,
                Name,
                NormalizedName,
                ParentFolderId,
                RootSourceId,
                IsAvailable,
                DirectSubfolderCount,
                DirectVideoFileCount
            FROM Folders
            WHERE
            """);

        commandText.AppendLine(searchCondition);

        if (rootSourceIds is not null)
        {
            commandText.AppendLine(
                $"AND RootSourceId IN ({string.Join(", ",
                    Enumerable.Range(0, rootSourceIds.Count)
                        .Select(index => $"$rootSourceId{index}"))})");
        }

        commandText.AppendLine(
            """
            ORDER BY
                CASE
                    WHEN NormalizedName = $normalizedQuery
                    THEN 0

                    WHEN instr(
                        NormalizedName,
                        $normalizedQuery) = 1
                    THEN 1

                    WHEN instr(
                        NormalizedName,
                        $normalizedQuery) > 0
                    THEN 2

                    ELSE 3
                END,
                length(Name),
                Name COLLATE NOCASE,
                Id
            LIMIT $maxResults;
            """);

        return commandText.ToString();
    }

    private static void AddRootSourceIdParameters(
        SqliteCommand command,
        IReadOnlyList<Guid>? rootSourceIds,
        string parameterPrefix = "rootSourceId")
    {
        if (rootSourceIds is null)
        {
            return;
        }

        for (var index = 0; index < rootSourceIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                $"${parameterPrefix}{index}",
                rootSourceIds[index].ToString("D"));
        }
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

    private static FolderSearchResult ReadResult(
        SqliteDataReader reader)
    {
        var rootSourceIdText = reader.GetString(5);

        if (!Guid.TryParse(
                rootSourceIdText,
                out var rootSourceId))
        {
            throw new InvalidDataException(
                "Invalid root source identifier: " +
                rootSourceIdText + ".");
        }

        return new FolderSearchResult(
            Id: reader.GetInt64(0),
            FullPath: reader.GetString(1),
            Name: reader.GetString(2),
            NormalizedName: reader.GetString(3),
            ParentFolderId: reader.IsDBNull(4)
                ? null
                : reader.GetInt64(4),
            RootSourceId: rootSourceId,
            IsAvailable: reader.GetInt64(6) != 0,
            DirectSubfolderCount: reader.GetInt32(7),
            DirectVideoFileCount: reader.GetInt32(8));
    }

    private static void ValidateQuery(
        FolderSearchQuery query)
    {
        if (query.Text is null)
        {
            throw new ArgumentException(
                "Search text cannot be null.",
                nameof(query));
        }

        if (!Enum.IsDefined(
                typeof(FolderSearchMode),
                query.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.");
        }

        if (query.MaxResults < 1 ||
            query.MaxResults > MaximumAllowedResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.MaxResults,
                $"Maximum result count must be between 1 " +
                $"and {MaximumAllowedResults}.");
        }
    }
}
