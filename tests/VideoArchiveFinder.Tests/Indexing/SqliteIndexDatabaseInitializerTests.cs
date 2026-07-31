using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteIndexDatabaseInitializerTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndFolderSchema()
    {
        var initializer = CreateInitializer();

        await initializer.InitializeAsync();

        var databasePath = GetDatabasePath();

        Assert.True(File.Exists(databasePath));

        await using var connection =
            new SqliteConnection($"Data Source={databasePath}");

        await connection.OpenAsync();

        Assert.Equal(
            3,
            await GetSchemaVersionAsync(connection));

        var tableNames = await ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table';
            """);

        Assert.Contains("Folders", tableNames);

        Assert.Contains(
            "FolderIndexingStates",
            tableNames);

        Assert.Contains(
            "VideoFiles",
            tableNames);

        var videoFileColumnNames = await ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('VideoFiles');");

        Assert.Contains("Id", videoFileColumnNames);
        Assert.Contains("FullPath", videoFileColumnNames);
        Assert.Contains("Name", videoFileColumnNames);
        Assert.Contains("NormalizedName", videoFileColumnNames);
        Assert.Contains("Extension", videoFileColumnNames);
        Assert.Contains("SizeBytes", videoFileColumnNames);
        Assert.Contains("LastWriteTimeUtc", videoFileColumnNames);
        Assert.Contains("FolderFullPath", videoFileColumnNames);
        Assert.Contains("RootSourceId", videoFileColumnNames);
        Assert.Contains("IsAvailable", videoFileColumnNames);
        Assert.Contains("LastSeenUtc", videoFileColumnNames);


        var columnNames = await ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('Folders');");

        Assert.Contains("Id", columnNames);
        Assert.Contains("FullPath", columnNames);
        Assert.Contains("Name", columnNames);
        Assert.Contains("NormalizedName", columnNames);
        Assert.Contains("SearchTokens", columnNames);
        Assert.Contains("SearchStems", columnNames);
        Assert.Contains("ParentFolderId", columnNames);
        Assert.Contains("RootSourceId", columnNames);
        Assert.Contains("IsAvailable", columnNames);
        Assert.Contains("LastSeenUtc", columnNames);
        Assert.Contains("DirectSubfolderCount", columnNames);
        Assert.Contains("DirectVideoFileCount", columnNames);


        var videoFileIndexNames = await ReadStringsAsync(
            connection,
            """
    SELECT name
    FROM sqlite_master
    WHERE type = 'index'
      AND tbl_name = 'VideoFiles';
    """);

        Assert.Contains(
            "UX_VideoFiles_RootSourceId_FullPath",
            videoFileIndexNames);

        Assert.Contains(
            "IX_VideoFiles_RootSourceId_FolderFullPath",
            videoFileIndexNames);

        Assert.Contains(
            "IX_VideoFiles_NormalizedName",
            videoFileIndexNames);


        var indexNames = await ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND tbl_name = 'Folders';
            """);

        Assert.Contains(
            "UX_Folders_RootSourceId_FullPath",
            indexNames);

        Assert.Contains(
            "IX_Folders_ParentFolderId",
            indexNames);

        Assert.Contains(
            "IX_Folders_RootSourceId",
            indexNames);

        Assert.Contains(
            "IX_Folders_NormalizedName",
            indexNames);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_PreservesData()
    {
        var initializer = CreateInitializer();

        await initializer.InitializeAsync();

        var databasePath = GetDatabasePath();

        await using (var connection =
            new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO Folders
                (
                    FullPath,
                    Name,
                    NormalizedName,
                    RootSourceId,
                    LastSeenUtc
                )
                VALUES
                (
                    'C:\Archive\Test',
                    'Test',
                    'test',
                    '00000000-0000-0000-0000-000000000001',
                    '2026-07-24T00:00:00+00:00'
                );
                """;

            await command.ExecuteNonQueryAsync();
        }

        await initializer.InitializeAsync();

        await using var verificationConnection =
            new SqliteConnection($"Data Source={databasePath}");

        await verificationConnection.OpenAsync();

        await using var countCommand =
            verificationConnection.CreateCommand();

        countCommand.CommandText =
            "SELECT COUNT(*) FROM Folders;";

        var folderCount = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync());

        Assert.Equal(1, folderCount);
        Assert.Equal(
            3,
            await GetSchemaVersionAsync(verificationConnection));
    }

    [Fact]
    public async Task InitializeAsync_WithCancellation_DoesNotCreateDatabase()
    {
        var initializer = CreateInitializer();

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => initializer.InitializeAsync(
                cancellationSource.Token));

        Assert.False(File.Exists(GetDatabasePath()));
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

    private SqliteIndexDatabaseInitializer CreateInitializer()
    {
        var directoryProvider =
            new TestApplicationDataDirectoryProvider(
                _temporaryDirectory);

        var pathProvider =
            new IndexDatabasePathProvider(directoryProvider);

        return new SqliteIndexDatabaseInitializer(
            pathProvider,
            NullLogger<SqliteIndexDatabaseInitializer>.Instance);
    }

    private string GetDatabasePath()
    {
        return Path.Combine(
            _temporaryDirectory,
            "Index",
            "video-archive-finder.db");
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA user_version;";

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }

    private static async Task<List<string>> ReadStringsAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = commandText;

        await using var reader =
            await command.ExecuteReaderAsync();

        var values = new List<string>();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
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
