using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteVideoMetadataSchemaMigrationTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_Version3Database_PreservesVideoFiles()
    {
        var databasePath =
            Path.Combine(
                _temporaryDirectory,
                "Index",
                "video-archive-finder.db");

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        var sourceId = Guid.NewGuid();

        await CreateVersion3DatabaseAsync(
            databasePath,
            sourceId);

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
            4,
            await GetSchemaVersionAsync(connection));

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                FullPath,
                HasVideoStream,
                DurationTicks,
                Width,
                Height,
                Codec,
                AnalysisState
            FROM VideoFiles;
            """;

        await using var reader =
            await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        Assert.Equal(
            @"C:\Archive\Folder\Video.mp4",
            reader.GetString(0));

        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));
        Assert.True(reader.IsDBNull(5));

        Assert.Equal(
            0,
            reader.GetInt32(6));

        Assert.False(await reader.ReadAsync());
    }

    private static async Task CreateVersion3DatabaseAsync(
        string databasePath,
        Guid sourceId)
    {
        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath}");

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE VideoFiles
            (
                Id INTEGER NOT NULL
                    PRIMARY KEY AUTOINCREMENT,

                FullPath TEXT NOT NULL COLLATE NOCASE,
                Name TEXT NOT NULL,
                NormalizedName TEXT NOT NULL,
                Extension TEXT NOT NULL COLLATE NOCASE,

                SizeBytes INTEGER NOT NULL
                    CHECK (SizeBytes >= 0),

                LastWriteTimeUtc TEXT NOT NULL,
                FolderFullPath TEXT NOT NULL COLLATE NOCASE,
                RootSourceId TEXT NOT NULL,

                IsAvailable INTEGER NOT NULL
                    CHECK (IsAvailable IN (0, 1)),

                LastSeenUtc TEXT NOT NULL
            );

            INSERT INTO VideoFiles
            (
                FullPath,
                Name,
                NormalizedName,
                Extension,
                SizeBytes,
                LastWriteTimeUtc,
                FolderFullPath,
                RootSourceId,
                IsAvailable,
                LastSeenUtc
            )
            VALUES
            (
                'C:\Archive\Folder\Video.mp4',
                'Video.mp4',
                'video.mp4',
                '.mp4',
                1000,
                '2026-08-14T10:00:00.0000000+00:00',
                'C:\Archive\Folder',
                $rootSourceId,
                1,
                '2026-08-14T12:00:00.0000000+00:00'
            );

            PRAGMA user_version = 3;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            sourceId.ToString());

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
