using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class
    SqliteThumbnailCacheStateRepositoryTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResetAllAsync_ClearsThumbnailStateAndPath()
    {
        var pathProvider =
            await CreateDatabaseAsync();

        await InsertVideoFileAsync(
            pathProvider,
            fullPath: @"C:\Archive\ready.mp4",
            thumbnailState:
                VideoFileThumbnailState.Succeeded,
            thumbnailPath:
                @"C:\Cache\ready.jpg");

        await InsertVideoFileAsync(
            pathProvider,
            fullPath: @"C:\Archive\clean.mp4",
            thumbnailState:
                VideoFileThumbnailState.NotGenerated,
            thumbnailPath: null);

        var repository =
            new SqliteThumbnailCacheStateRepository(
                pathProvider,
                NullLogger<
                    SqliteThumbnailCacheStateRepository>
                    .Instance);

        var updatedCount =
            await repository.ResetAllAsync();

        Assert.Equal(1, updatedCount);

        await using var connection =
            CreateConnection(pathProvider);

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                ThumbnailState,
                ThumbnailPath
            FROM VideoFiles
            WHERE FullPath = $fullPath;
            """;

        command.Parameters.AddWithValue(
            "$fullPath",
            @"C:\Archive\ready.mp4");

        await using var reader =
            await command.ExecuteReaderAsync();

        Assert.True(
            await reader.ReadAsync());

        Assert.Equal(
            (int)VideoFileThumbnailState.NotGenerated,
            reader.GetInt32(0));

        Assert.True(
            reader.IsDBNull(1));
    }

    private async Task<IndexDatabasePathProvider>
        CreateDatabaseAsync()
    {
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
                    SqliteIndexDatabaseInitializer>
                    .Instance);

        await initializer.InitializeAsync();

        return pathProvider;
    }

    private static async Task InsertVideoFileAsync(
        IndexDatabasePathProvider pathProvider,
        string fullPath,
        VideoFileThumbnailState thumbnailState,
        string? thumbnailPath)
    {
        await using var connection =
            CreateConnection(pathProvider);

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
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
                ThumbnailState,
                ThumbnailPath
            )
            VALUES
            (
                $fullPath,
                $name,
                $normalizedName,
                '.mp4',
                1024,
                '2026-08-21T00:00:00+00:00',
                'C:\Archive',
                '00000000-0000-0000-0000-000000000001',
                1,
                '2026-08-21T00:00:00+00:00',
                $thumbnailState,
                $thumbnailPath
            );
            """;

        command.Parameters.AddWithValue(
            "$fullPath",
            fullPath);

        command.Parameters.AddWithValue(
            "$name",
            Path.GetFileName(fullPath));

        command.Parameters.AddWithValue(
            "$normalizedName",
            Path.GetFileName(fullPath)
                .ToLowerInvariant());

        command.Parameters.AddWithValue(
            "$thumbnailState",
            (int)thumbnailState);

        command.Parameters.AddWithValue(
            "$thumbnailPath",
            thumbnailPath is null
                ? DBNull.Value
                : thumbnailPath);

        await command.ExecuteNonQueryAsync();
    }

    private static SqliteConnection CreateConnection(
        IndexDatabasePathProvider pathProvider)
    {
        return new SqliteConnection(
            $"Data Source={pathProvider.GetDatabasePath()}");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(
            _temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private sealed class
        TestApplicationDataDirectoryProvider(
            string directoryPath)
        : IApplicationDataDirectoryProvider
    {
        public string GetApplicationDataDirectory()
        {
            return directoryPath;
        }
    }
}
