using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SqliteFolderIndexCleanupService
    : IFolderIndexCleanupService
{
    private readonly IndexDatabasePathProvider _databasePathProvider;

    private readonly ILogger<SqliteFolderIndexCleanupService> _logger;

    public SqliteFolderIndexCleanupService(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteFolderIndexCleanupService> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task DeleteByRootSourceIdsAsync(
        IReadOnlyCollection<Guid> rootSourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootSourceIds);

        var distinctSourceIds = rootSourceIds
            .Distinct()
            .ToArray();

        if (distinctSourceIds.Length == 0)
        {
            return;
        }

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var transaction =
            connection.BeginTransaction();


        try
        {
            var deletedFolderCount =
                await DeleteFoldersAsync(
                        connection,
                        transaction,
                        distinctSourceIds,
                        cancellationToken)
                    .ConfigureAwait(false);

            var deletedStateCount =
                await DeleteIndexingStatesAsync(
                        connection,
                        transaction,
                        distinctSourceIds,
                        cancellationToken)
                    .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Deleted index data for {SourceCount} archive sources. " +
                "Folders: {FolderCount}; indexing states: {StateCount}.",
                distinctSourceIds.Length,
                deletedFolderCount,
                deletedStateCount);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }
    }

    private static async Task<int> DeleteFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Guid> rootSourceIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            CreateDeleteCommandText(
                tableName: "Folders",
                rootSourceIds.Count);

        AddSourceIdParameters(
            command,
            rootSourceIds);

        return await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> DeleteIndexingStatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Guid> rootSourceIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            CreateDeleteCommandText(
                tableName: "FolderIndexingStates",
                rootSourceIds.Count);

        AddSourceIdParameters(
            command,
            rootSourceIds);

        return await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string CreateDeleteCommandText(
        string tableName,
        int sourceIdCount)
    {
        var parameterNames = Enumerable
            .Range(0, sourceIdCount)
            .Select(index => $"@sourceId{index}");

        return
            $"DELETE FROM {tableName} " +
            $"WHERE RootSourceId IN ({string.Join(", ", parameterNames)});";
    }

    private static void AddSourceIdParameters(
        SqliteCommand command,
        IReadOnlyList<Guid> rootSourceIds)
    {
        for (var index = 0; index < rootSourceIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                $"@sourceId{index}",
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
                Cache = SqliteCacheMode.Shared
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
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
