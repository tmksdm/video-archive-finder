using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SqliteIndexDatabaseInitializer
    : IIndexDatabaseInitializer
{
    private const int CurrentSchemaVersion = 4;

    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ILogger<SqliteIndexDatabaseInitializer>
        _logger;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    public SqliteIndexDatabaseInitializer(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteIndexDatabaseInitializer> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializationLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var databasePath =
                _databasePathProvider.GetDatabasePath();

            var databaseDirectory = Path.GetDirectoryName(
                databasePath);

            if (string.IsNullOrWhiteSpace(databaseDirectory))
            {
                throw new InvalidOperationException(
                    "Не удалось определить папку базы индекса.");
            }

            Directory.CreateDirectory(databaseDirectory);

            var connectionString =
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString();

            await using var connection =
                new SqliteConnection(connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            var schemaVersion =
                await GetSchemaVersionAsync(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Версия базы индекса {schemaVersion} новее " +
                    $"поддерживаемой версии {CurrentSchemaVersion}.");
            }

            if (schemaVersion == 0)
            {
                await CreateInitialSchemaAsync(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

                schemaVersion = 2;
            }

            if (schemaVersion == 1)
            {
                await MigrateFromVersion1ToVersion2Async(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

                schemaVersion = 2;
            }

            if (schemaVersion == 2)
            {
                await SqliteVideoFileSchemaMigration
                    .MigrateFromVersion2ToVersion3Async(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

                schemaVersion = 3;
            }

            if (schemaVersion == 3)
            {
                await SqliteVideoMetadataSchemaMigration
                    .MigrateFromVersion3ToVersion4Async(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);
            }




            _logger.LogInformation(
                "Index database initialized at {DatabasePath}. " +
                "Schema version: {SchemaVersion}.",
                databasePath,
                CurrentSchemaVersion);
        }
        finally
        {
            _initializationLock.Release();
        }
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

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA user_version;";

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(result);
    }

    private static async Task CreateInitialSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            connection.BeginTransaction();

        try
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText =
                """
                CREATE TABLE Folders
                (
                    Id INTEGER NOT NULL
                        CONSTRAINT PK_Folders PRIMARY KEY AUTOINCREMENT,

                    FullPath TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL,
                    SearchTokens TEXT NOT NULL DEFAULT '',
                    SearchStems TEXT NOT NULL DEFAULT '',

                    ParentFolderId INTEGER NULL,
                    RootSourceId TEXT NOT NULL,

                    IsAvailable INTEGER NOT NULL DEFAULT 1
                        CHECK (IsAvailable IN (0, 1)),

                    LastSeenUtc TEXT NOT NULL,

                    DirectSubfolderCount INTEGER NOT NULL DEFAULT 0
                        CHECK (DirectSubfolderCount >= 0),

                    DirectVideoFileCount INTEGER NOT NULL DEFAULT 0
                        CHECK (DirectVideoFileCount >= 0),

                    CONSTRAINT FK_Folders_ParentFolder
                        FOREIGN KEY (ParentFolderId)
                        REFERENCES Folders(Id)
                        ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX
                    UX_Folders_RootSourceId_FullPath
                ON Folders(RootSourceId, FullPath);

                CREATE INDEX
                    IX_Folders_ParentFolderId
                ON Folders(ParentFolderId);

                CREATE INDEX
                    IX_Folders_RootSourceId
                ON Folders(RootSourceId);

                CREATE INDEX
                    IX_Folders_NormalizedName
                ON Folders(NormalizedName);

                CREATE TABLE FolderIndexingStates
                (
                    RootSourceId TEXT NOT NULL
                        CONSTRAINT PK_FolderIndexingStates PRIMARY KEY,

                    DiscoveredFolderCount INTEGER NOT NULL
                        CHECK (DiscoveredFolderCount >= 0),

                    IndexedFolderCount INTEGER NOT NULL
                        CHECK (IndexedFolderCount >= 0),

                    ErrorCount INTEGER NOT NULL
                        CHECK (ErrorCount >= 0),

                    StartedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT NOT NULL
                );

                PRAGMA user_version = 2;
                """;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }
    }

    private static async Task MigrateFromVersion1ToVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            connection.BeginTransaction();

        try
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText =
                """
            CREATE TABLE FolderIndexingStates
            (
                RootSourceId TEXT NOT NULL
                    CONSTRAINT PK_FolderIndexingStates PRIMARY KEY,

                DiscoveredFolderCount INTEGER NOT NULL
                    CHECK (DiscoveredFolderCount >= 0),

                IndexedFolderCount INTEGER NOT NULL
                    CHECK (IndexedFolderCount >= 0),

                ErrorCount INTEGER NOT NULL
                    CHECK (ErrorCount >= 0),

                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NOT NULL
            );

            PRAGMA user_version = 2;
            """;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }
    }

}
