using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteFolderIndexCleanupServiceTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeleteByRootSourceIdsAsync_DeletesFoldersAndState()
    {
        var services = await CreateServicesAsync();
        var sourceId = Guid.NewGuid();

        await AddFolderAndStateAsync(
            services,
            sourceId,
            @"C:\Archive");

        await services.CleanupService
            .DeleteByRootSourceIdsAsync([sourceId]);

        var remainingFolders =
            await services.FolderRepository
                .GetByRootSourceIdAsync(sourceId);

        var remainingState =
            await services.StateRepository
                .GetAsync(sourceId);

        Assert.Empty(remainingFolders);
        Assert.Null(remainingState);
    }

    [Fact]
    public async Task DeleteByRootSourceIdsAsync_DoesNotDeleteOtherSource()
    {
        var services = await CreateServicesAsync();

        var removedSourceId = Guid.NewGuid();
        var retainedSourceId = Guid.NewGuid();

        await AddFolderAndStateAsync(
            services,
            removedSourceId,
            @"C:\Removed");

        await AddFolderAndStateAsync(
            services,
            retainedSourceId,
            @"D:\Retained");

        await services.CleanupService
            .DeleteByRootSourceIdsAsync([removedSourceId]);

        Assert.Empty(
            await services.FolderRepository
                .GetByRootSourceIdAsync(removedSourceId));

        Assert.Null(
            await services.StateRepository
                .GetAsync(removedSourceId));

        Assert.Single(
            await services.FolderRepository
                .GetByRootSourceIdAsync(retainedSourceId));

        Assert.NotNull(
            await services.StateRepository
                .GetAsync(retainedSourceId));
    }

    [Fact]
    public async Task DeleteByRootSourceIdsAsync_EmptyList_DoesNothing()
    {
        var services = await CreateServicesAsync();
        var sourceId = Guid.NewGuid();

        await AddFolderAndStateAsync(
            services,
            sourceId,
            @"C:\Archive");

        await services.CleanupService
            .DeleteByRootSourceIdsAsync([]);

        Assert.Single(
            await services.FolderRepository
                .GetByRootSourceIdAsync(sourceId));

        Assert.NotNull(
            await services.StateRepository
                .GetAsync(sourceId));
    }

    private async Task<TestServices> CreateServicesAsync()
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

        var folderRepository =
            new SqliteFolderIndexRepository(
                pathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        var stateRepository =
            new SqliteFolderIndexingStateRepository(
                pathProvider,
                NullLogger<
                    SqliteFolderIndexingStateRepository>.Instance);

        var cleanupService =
            new SqliteFolderIndexCleanupService(
                pathProvider,
                NullLogger<
                    SqliteFolderIndexCleanupService>.Instance);

        return new TestServices(
            folderRepository,
            stateRepository,
            cleanupService);
    }

    private static async Task AddFolderAndStateAsync(
        TestServices services,
        Guid sourceId,
        string fullPath)
    {
        var timestamp = DateTimeOffset.UtcNow;

        await services.FolderRepository.UpsertBatchAsync(
        [
            new FolderIndexUpsertItem(
                FullPath: fullPath,
                Name: Path.GetFileName(fullPath),
                NormalizedName:
                    Path.GetFileName(fullPath).ToLowerInvariant(),
                SearchTokens:
                    Path.GetFileName(fullPath).ToLowerInvariant(),
                SearchStems: string.Empty,
                ParentFullPath: null,
                RootSourceId: sourceId,
                IsAvailable: true,
                LastSeenUtc: timestamp,
                DirectSubfolderCount: 0,
                DirectVideoFileCount: 0)
        ]);

        await services.StateRepository.SaveAsync(
            new FolderIndexingState(
                RootSourceId: sourceId,
                DiscoveredFolderCount: 1,
                IndexedFolderCount: 1,
                ErrorCount: 0,
                StartedAtUtc: timestamp,
                CompletedAtUtc: timestamp));
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

    private sealed record TestServices(
        SqliteFolderIndexRepository FolderRepository,
        SqliteFolderIndexingStateRepository StateRepository,
        SqliteFolderIndexCleanupService CleanupService);

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
