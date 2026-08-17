using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteVideoThumbnailSchemaMigrationTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_Version4Database_PreservesVideoFiles()
    {
        var databasePath =
            Path.Combine(
                _temporaryDirectory,
                "Index",
                "video-archive-finder.db");

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        var sourceId = Guid.NewGuid();

        await CreateVersion4DatabaseAsync(
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
            5,
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
                AnalysisState,
                ThumbnailState,
                ThumbnailPath
            FROM VideoFiles;
            """;

        await using var reader =
            await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        Assert.Equal(
            @"C:\Archive\Folder\Video.mp4",
            reader.GetString(0));

        Assert.Equal(
            1,
            reader.GetInt32(1));

        Assert.Equal(
            TimeSpan.FromMinutes(2).Ticks,
            reader.GetInt64(2));

        Assert.Equal(
            1920,
            reader.GetInt32(3));

        Assert.Equal(
            1080,
            reader.GetInt32(4));

        Assert.Equal(
            "h264",
            reader.GetString(5));

        Assert.Equal(
            2,
            reader.GetInt32(6));

        Assert.Equal(
            0,
            reader.GetInt32(7));

        Assert.True(reader.IsDBNull(8));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task CreateVersion4DatabaseAsync(
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

                LastSeenUtc TEXT NOT NULL,

                HasVideoStream INTEGER NULL
                    CHECK
                    (
                        HasVideoStream IS NULL OR
                        HasVideoStream IN (0, 1)
                    ),

                DurationTicks INTEGER NULL
                    CHECK
                    (
                        DurationTicks IS NULL OR
                        DurationTicks >= 0
                    ),

                Width INTEGER NULL
                    CHECK
                    (
                        Width IS NULL OR
                        Width > 0
                    ),

                Height INTEGER NULL
                    CHECK
                    (
                        Height IS NULL OR
                        Height > 0
                    ),

                Codec TEXT NULL,

                AnalysisState INTEGER NOT NULL
                    DEFAULT 0
                    CHECK (AnalysisState IN (0, 1, 2))
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
                LastSeenUtc,
                HasVideoStream,
                DurationTicks,
                Width,
                Height,
                Codec,
                AnalysisState
            )
            VALUES
            (
                'C:\Archive\Folder\Video.mp4',
                'Video.mp4',
                'video.mp4',
                '.mp4',
                1000,
                '2026-08-17T10:00:00.0000000+00:00',
                'C:\Archive\Folder',
                $rootSourceId,
                1,
                '2026-08-17T12:00:00.0000000+00:00',
                1,
                $durationTicks,
                1920,
                1080,
                'h264',
                2
            );

            PRAGMA user_version = 4;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            sourceId.ToString("D"));

        command.Parameters.AddWithValue(
            "$durationTicks",
            TimeSpan.FromMinutes(2).Ticks);

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
