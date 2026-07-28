using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SqliteFolderIndexingStateRepository
    : IFolderIndexingStateRepository
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ILogger<SqliteFolderIndexingStateRepository>
        _logger;

    public SqliteFolderIndexingStateRepository(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteFolderIndexingStateRepository> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task SaveAsync(
        FolderIndexingState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO FolderIndexingStates
            (
                RootSourceId,
                DiscoveredFolderCount,
                IndexedFolderCount,
                ErrorCount,
                StartedAtUtc,
                CompletedAtUtc
            )
            VALUES
            (
                $rootSourceId,
                $discoveredFolderCount,
                $indexedFolderCount,
                $errorCount,
                $startedAtUtc,
                $completedAtUtc
            )
            ON CONFLICT(RootSourceId) DO UPDATE SET
                DiscoveredFolderCount =
                    excluded.DiscoveredFolderCount,
                IndexedFolderCount =
                    excluded.IndexedFolderCount,
                ErrorCount =
                    excluded.ErrorCount,
                StartedAtUtc =
                    excluded.StartedAtUtc,
                CompletedAtUtc =
                    excluded.CompletedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            state.RootSourceId.ToString("D"));

        command.Parameters.AddWithValue(
            "$discoveredFolderCount",
            state.DiscoveredFolderCount);

        command.Parameters.AddWithValue(
            "$indexedFolderCount",
            state.IndexedFolderCount);

        command.Parameters.AddWithValue(
            "$errorCount",
            state.ErrorCount);

        command.Parameters.AddWithValue(
            "$startedAtUtc",
            state.StartedAtUtc
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture));

        command.Parameters.AddWithValue(
            "$completedAtUtc",
            state.CompletedAtUtc
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture));

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Saved folder indexing state for source {SourceId}.",
            state.RootSourceId);
    }

    public async Task<FolderIndexingState?> GetAsync(
        Guid rootSourceId,
        CancellationToken cancellationToken = default)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                RootSourceId,
                DiscoveredFolderCount,
                IndexedFolderCount,
                ErrorCount,
                StartedAtUtc,
                CompletedAtUtc
            FROM FolderIndexingStates
            WHERE RootSourceId = $rootSourceId;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            rootSourceId.ToString("D"));

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        return new FolderIndexingState(
            RootSourceId:
                Guid.Parse(reader.GetString(0)),
            DiscoveredFolderCount:
                reader.GetInt32(1),
            IndexedFolderCount:
                reader.GetInt32(2),
            ErrorCount:
                reader.GetInt32(3),
            StartedAtUtc:
                ParseTimestamp(reader.GetString(4)),
            CompletedAtUtc:
                ParseTimestamp(reader.GetString(5)));
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

    private static DateTimeOffset ParseTimestamp(
        string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static void ValidateState(
        FolderIndexingState state)
    {
        if (state.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(state));
        }

        if (state.DiscoveredFolderCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Discovered folder count cannot be negative.");
        }

        if (state.IndexedFolderCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Indexed folder count cannot be negative.");
        }

        if (state.ErrorCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Error count cannot be negative.");
        }

        if (state.CompletedAtUtc < state.StartedAtUtc)
        {
            throw new ArgumentException(
                "Completion time cannot precede start time.",
                nameof(state));
        }
    }
}
