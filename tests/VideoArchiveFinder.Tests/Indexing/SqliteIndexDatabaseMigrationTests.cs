using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteIndexDatabaseMigrationTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_Version1Database_MigratesWithoutLosingFolders()
    {
        Directory.CreateDirectory(_temporaryDirectory);

        var databasePath =
            Path.Combine(
                _temporaryDirectory,
                "Index",
                "video-archive-finder.db");

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        await CreateVersion1DatabaseAsync(databasePath);


        var directoryProvider =
            new TestApplicationDataDirectoryProvider(
                _temporaryDirectory);

        var pathProvider =
            new IndexDatabasePathProvider(
                directoryProvider);

        var initializer =
            new SqliteIndexDatabaseInitializer(
                pathProvider,
                NullLogger<
                    SqliteIndexDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath}");

        await connection.OpenAsync();

        Assert.Equal(
            3,
            await GetSchemaVersionAsync(connection));

        Assert.True(
            await TableExistsAsync(
                connection,
                "FolderIndexingStates"));

        Assert.True(
            await TableExistsAsync(
                connection,
                "VideoFiles"));


        await using var countCommand =
            connection.CreateCommand();

        countCommand.CommandText =
            "SELECT COUNT(*) FROM Folders;";

        var folderCount =
            Convert.ToInt32(
                await countCommand.ExecuteScalarAsync());

        Assert.Equal(1, folderCount);
    }

    private static async Task CreateVersion1DatabaseAsync(
        string databasePath)
    {
        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath}");

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE Folders
            (
                Id TEXT NOT NULL PRIMARY KEY,
                FullPath TEXT NOT NULL
            );

            INSERT INTO Folders
            (
                Id,
                FullPath
            )
            VALUES
            (
                'existing-folder',
                'C:\Archive'
            );

            PRAGMA user_version = 1;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA user_version;";

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tableName;
            """;

        command.Parameters.AddWithValue(
            "$tableName",
            tableName);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync()) == 1;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private sealed class TestApplicationDataDirectoryProvider
        : IApplicationDataDirectoryProvider
    {
        private readonly string _directoryPath;

        public TestApplicationDataDirectoryProvider(
            string directoryPath)
        {
            _directoryPath = directoryPath;
        }

        public string GetApplicationDataDirectory()
        {
            return _directoryPath;
        }
    }
}
