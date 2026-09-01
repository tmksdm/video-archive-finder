using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SqliteFolderIndexRepositoryTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpsertBatchAsync_WritesAndReadsFoldersBySource()
    {
        var repository = await CreateRepositoryAsync();

        var firstSourceId = Guid.NewGuid();
        var secondSourceId = Guid.NewGuid();

        var lastSeenUtc =
            new DateTimeOffset(
                2026,
                7,
                25,
                10,
                30,
                0,
                TimeSpan.Zero);

        var folders = new[]
        {
            CreateFolder(
                fullPath: @"C:\Archive",
                name: "Archive",
                rootSourceId: firstSourceId,
                lastSeenUtc: lastSeenUtc,
                directSubfolderCount: 1),

            CreateFolder(
                fullPath: @"C:\Archive\Почта",
                name: "Почта",
                rootSourceId: firstSourceId,
                parentFullPath: @"C:\Archive",
                normalizedName: "почта",
                searchTokens: "почта",
                searchStems: "почт",
                lastSeenUtc: lastSeenUtc,
                directVideoFileCount: 3),

            CreateFolder(
                fullPath: @"D:\Other",
                name: "Other",
                rootSourceId: secondSourceId,
                lastSeenUtc: lastSeenUtc)
        };

        await repository.UpsertBatchAsync(folders);

        var firstSourceFolders =
            await repository.GetByRootSourceIdAsync(
                firstSourceId);

        Assert.Equal(2, firstSourceFolders.Count);

        var mailFolder = Assert.Single(
            firstSourceFolders,
            folder => folder.FullPath ==
                @"C:\Archive\Почта");

        Assert.Equal("Почта", mailFolder.Name);
        Assert.Equal("почта", mailFolder.NormalizedName);
        Assert.Equal("почта", mailFolder.SearchTokens);
        Assert.Equal("почт", mailFolder.SearchStems);
        Assert.Equal(firstSourceId, mailFolder.RootSourceId);
        Assert.True(mailFolder.IsAvailable);
        Assert.Equal(lastSeenUtc, mailFolder.LastSeenUtc);
        Assert.Equal(0, mailFolder.DirectSubfolderCount);
        Assert.Equal(3, mailFolder.DirectVideoFileCount);

        var secondSourceFolders =
            await repository.GetByRootSourceIdAsync(
                secondSourceId);

        Assert.Single(secondSourceFolders);
        Assert.Equal(
            @"D:\Other",
            secondSourceFolders[0].FullPath);
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsOnlyDirectChildrenInNameOrder()
    {
        var repository = await CreateRepositoryAsync();
        var sourceId = Guid.NewGuid();

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId),
            CreateFolder(
                @"C:\Archive\Телефон",
                "Телефон",
                sourceId,
                parentFullPath: @"C:\Archive",
                directVideoFileCount: 50),
            CreateFolder(
                @"C:\Archive\Звук",
                "Звук",
                sourceId,
                parentFullPath: @"C:\Archive",
                directSubfolderCount: 1),
            CreateFolder(
                @"C:\Archive\Телефон\Камера",
                "Камера",
                sourceId,
                parentFullPath: @"C:\Archive\Телефон")
        ]);

        var parent = Assert.Single(
            await repository.GetByRootSourceIdAsync(sourceId),
            folder => folder.FullPath == @"C:\Archive");

        var children = await repository.GetChildrenAsync(
            parent.Id);

        Assert.Equal(2, children.Count);
        Assert.Equal("Звук", children[0].Name);
        Assert.Equal("Телефон", children[1].Name);
        Assert.Equal(50, children[1].DirectVideoFileCount);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenFolderExists_UpdatesIt()
    {
        var repository = await CreateRepositoryAsync();
        var sourceId = Guid.NewGuid();

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                fullPath: @"C:\Archive\Folder",
                name: "Folder",
                rootSourceId: sourceId,
                normalizedName: "folder",
                searchTokens: "folder",
                searchStems: "folder",
                directSubfolderCount: 1,
                directVideoFileCount: 2)
        ]);

        var originalFolder = Assert.Single(
            await repository.GetByRootSourceIdAsync(
                sourceId));

        var updatedLastSeenUtc =
            new DateTimeOffset(
                2026,
                7,
                26,
                12,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                fullPath: @"C:\Archive\Folder",
                name: "Папка",
                rootSourceId: sourceId,
                normalizedName: "папка",
                searchTokens: "папка",
                searchStems: "папк",
                isAvailable: false,
                lastSeenUtc: updatedLastSeenUtc,
                directSubfolderCount: 4,
                directVideoFileCount: 7)
        ]);

        var updatedFolder = Assert.Single(
            await repository.GetByRootSourceIdAsync(
                sourceId));

        Assert.Equal(originalFolder.Id, updatedFolder.Id);
        Assert.Equal("Папка", updatedFolder.Name);
        Assert.Equal("папка", updatedFolder.NormalizedName);
        Assert.Equal("папка", updatedFolder.SearchTokens);
        Assert.Equal("папк", updatedFolder.SearchStems);
        Assert.False(updatedFolder.IsAvailable);
        Assert.Equal(
            updatedLastSeenUtc,
            updatedFolder.LastSeenUtc);
        Assert.Equal(4, updatedFolder.DirectSubfolderCount);
        Assert.Equal(7, updatedFolder.DirectVideoFileCount);
    }

    [Fact]
    public async Task UpsertBatchAsync_SetsParentRelationshipRegardlessOfOrder()
    {
        var repository = await CreateRepositoryAsync();
        var sourceId = Guid.NewGuid();

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                fullPath: @"C:\Archive\Parent\Child",
                name: "Child",
                rootSourceId: sourceId,
                parentFullPath: @"C:\Archive\Parent"),

            CreateFolder(
                fullPath: @"C:\Archive\Parent",
                name: "Parent",
                rootSourceId: sourceId,
                parentFullPath: @"C:\Archive")
        ]);

        var folders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        var parent = Assert.Single(
            folders,
            folder => folder.FullPath ==
                @"C:\Archive\Parent");

        var child = Assert.Single(
            folders,
            folder => folder.FullPath ==
                @"C:\Archive\Parent\Child");

        Assert.Equal(parent.Id, child.ParentFolderId);
        Assert.Null(parent.ParentFolderId);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenRepeated_DoesNotCreateDuplicates()
    {
        var repository = await CreateRepositoryAsync();
        var sourceId = Guid.NewGuid();

        var folder = CreateFolder(
            fullPath: @"\\server\share\Archive",
            name: "Archive",
            rootSourceId: sourceId);

        await repository.UpsertBatchAsync([folder]);
        await repository.UpsertBatchAsync([folder]);

        var storedFolders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        Assert.Single(storedFolders);
    }

    [Fact]
    public async Task UpsertBatchAsync_WithCancellation_DoesNotWriteFolders()
    {
        var repository = await CreateRepositoryAsync();
        var sourceId = Guid.NewGuid();

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.UpsertBatchAsync(
            [
                CreateFolder(
                    fullPath: @"C:\Archive",
                    name: "Archive",
                    rootSourceId: sourceId)
            ],
            cancellationSource.Token));

        var storedFolders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        Assert.Empty(storedFolders);
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

    private async Task<SqliteFolderIndexRepository>
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

        return new SqliteFolderIndexRepository(
            pathProvider,
            NullLogger<
                SqliteFolderIndexRepository>.Instance);
    }

    private static FolderIndexUpsertItem CreateFolder(
        string fullPath,
        string name,
        Guid rootSourceId,
        string? parentFullPath = null,
        string? normalizedName = null,
        string searchTokens = "",
        string searchStems = "",
        bool isAvailable = true,
        DateTimeOffset? lastSeenUtc = null,
        int directSubfolderCount = 0,
        int directVideoFileCount = 0)
    {
        return new FolderIndexUpsertItem(
            FullPath: fullPath,
            Name: name,
            NormalizedName:
                normalizedName ?? name.ToLowerInvariant(),
            SearchTokens: searchTokens,
            SearchStems: searchStems,
            ParentFullPath: parentFullPath,
            RootSourceId: rootSourceId,
            IsAvailable: isAvailable,
            LastSeenUtc:
                lastSeenUtc ?? DateTimeOffset.UtcNow,
            DirectSubfolderCount: directSubfolderCount,
            DirectVideoFileCount: directVideoFileCount);
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
