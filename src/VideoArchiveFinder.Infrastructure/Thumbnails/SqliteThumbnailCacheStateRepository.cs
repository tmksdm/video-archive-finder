using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class SqliteThumbnailCacheStateRepository
    : IThumbnailCacheStateRepository
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ILogger<
        SqliteThumbnailCacheStateRepository> _logger;

    public SqliteThumbnailCacheStateRepository(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<
            SqliteThumbnailCacheStateRepository> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task<int> ResetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            UPDATE VideoFiles
            SET
                ThumbnailState = $notGenerated,
                ThumbnailPath = NULL
            WHERE
                ThumbnailState <> $notGenerated
                OR ThumbnailPath IS NOT NULL;
            """;

        command.Parameters.AddWithValue(
            "$notGenerated",
            (int)VideoFileThumbnailState.NotGenerated);

        var updatedCount =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        _logger.LogInformation(
            "Reset thumbnail cache state for " +
            "{VideoFileCount} indexed video files.",
            updatedCount);

        return updatedCount;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePathProvider
                        .GetDatabasePath(),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

        return new SqliteConnection(
            connectionString);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

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
