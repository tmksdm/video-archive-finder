using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteFolderIndexingStateRepositoryTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_UnknownSource_ReturnsNull()
    {
        var repository =
            await CreateRepositoryAsync();

        var result =
            await repository.GetAsync(
                Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_WritesAndReadsState()
    {
        var repository =
            await CreateRepositoryAsync();

        var state = CreateState(
            rootSourceId: Guid.NewGuid(),
            discoveredFolderCount: 301,
            indexedFolderCount: 301,
            errorCount: 2,
            startedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    8,
                    10,
                    0,
                    TimeSpan.Zero),
            completedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    8,
                    12,
                    30,
                    TimeSpan.Zero));

        await repository.SaveAsync(state);

        var restored =
            await repository.GetAsync(
                state.RootSourceId);

        Assert.Equal(state, restored);
    }

    [Fact]
    public async Task SaveAsync_ExistingSource_UpdatesSingleState()
    {
        var repository =
            await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();

        var firstState = CreateState(
            rootSourceId: sourceId,
            discoveredFolderCount: 10,
            indexedFolderCount: 10,
            errorCount: 1,
            startedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    8,
                    0,
                    0,
                    TimeSpan.Zero),
            completedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    8,
                    1,
                    0,
                    TimeSpan.Zero));

        var updatedState = CreateState(
            rootSourceId: sourceId,
            discoveredFolderCount: 25,
            indexedFolderCount: 25,
            errorCount: 0,
            startedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    9,
                    0,
                    0,
                    TimeSpan.Zero),
            completedAtUtc:
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    9,
                    2,
                    0,
                    TimeSpan.Zero));

        await repository.SaveAsync(firstState);
        await repository.SaveAsync(updatedState);

        var restored =
            await repository.GetAsync(sourceId);

        Assert.Equal(updatedState, restored);

        Assert.Equal(
            1,
            await CountStatesAsync());
    }

    private async Task<
        SqliteFolderIndexingStateRepository>
        CreateRepositoryAsync()
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
                    SqliteIndexDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        return new SqliteFolderIndexingStateRepository(
            pathProvider,
            NullLogger<
                SqliteFolderIndexingStateRepository>.Instance);
    }

    private async Task<int> CountStatesAsync()
    {
        var databasePath =
            Path.Combine(
                _temporaryDirectory,
                "Index",
                "video-archive-finder.db");


        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath}");

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM FolderIndexingStates;
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }

    private static FolderIndexingState CreateState(
        Guid rootSourceId,
        int discoveredFolderCount,
        int indexedFolderCount,
        int errorCount,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return new FolderIndexingState(
            RootSourceId:
                rootSourceId,
            DiscoveredFolderCount:
                discoveredFolderCount,
            IndexedFolderCount:
                indexedFolderCount,
            ErrorCount:
                errorCount,
            StartedAtUtc:
                startedAtUtc,
            CompletedAtUtc:
                completedAtUtc);
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
