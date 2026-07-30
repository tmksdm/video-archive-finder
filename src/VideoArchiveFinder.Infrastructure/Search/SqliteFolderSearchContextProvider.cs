using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Infrastructure.Search;

public sealed class SqliteFolderSearchContextProvider
    : IFolderSearchContextProvider
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ILogger<SqliteFolderSearchContextProvider>
        _logger;

    public SqliteFolderSearchContextProvider(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteFolderSearchContextProvider> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FolderSearchResult>>
        GetContextFoldersAsync(
            IReadOnlyCollection<FolderSearchResult> matches,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);

        cancellationToken.ThrowIfCancellationRequested();

        if (matches.Count == 0)
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

            await using var command = connection.CreateCommand();

            var folderIds = matches
                .Select(match => match.Id)
                .Distinct()
                .ToArray();

            var parameterNames = folderIds
                .Select((_, index) => $"$folderId{index}")
                .ToArray();

            var parameterList =
                string.Join(", ", parameterNames);

            command.CommandText =
                $"""
                WITH RECURSIVE FolderContext AS
                (
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
                    WHERE Id IN ({parameterList})

                    UNION

                    SELECT
                        parent.Id,
                        parent.FullPath,
                        parent.Name,
                        parent.NormalizedName,
                        parent.ParentFolderId,
                        parent.RootSourceId,
                        parent.IsAvailable,
                        parent.DirectSubfolderCount,
                        parent.DirectVideoFileCount
                    FROM Folders AS parent
                    INNER JOIN FolderContext AS child
                        ON child.ParentFolderId = parent.Id
                )
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
                FROM FolderContext;
                """;

            for (var index = 0;
                 index < folderIds.Length;
                 index++)
            {
                command.Parameters.AddWithValue(
                    parameterNames[index],
                    folderIds[index]);
            }

            await using var reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            var folders = new List<FolderSearchResult>();

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                folders.Add(ReadFolder(reader));
            }

            return folders;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Loading folder search context was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not load folder search context.");

            throw;
        }
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

    private static FolderSearchResult ReadFolder(
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
}
