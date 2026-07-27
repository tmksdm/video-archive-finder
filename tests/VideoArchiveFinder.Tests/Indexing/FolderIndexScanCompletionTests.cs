using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class FolderIndexScanCompletionTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompleteScanAsync_RemovesOnlyFoldersMissingFromNewScan()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var otherSourceId = Guid.NewGuid();

        var previousScanUtc =
            new DateTimeOffset(
                2026,
                7,
                27,
                9,
                0,
                0,
                TimeSpan.Zero);

        var currentScanUtc =
            previousScanUtc.AddHours(1);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId,
                previousScanUtc),

            CreateFolder(
                @"C:\Archive\Existing",
                "Existing",
                sourceId,
                previousScanUtc,
                @"C:\Archive"),

            CreateFolder(
                @"C:\Archive\Removed",
                "Removed",
                sourceId,
                previousScanUtc,
                @"C:\Archive"),

            CreateFolder(
                @"D:\Other",
                "Other",
                otherSourceId,
                previousScanUtc)
        ]);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId,
                currentScanUtc),

            CreateFolder(
                @"C:\Archive\Existing",
                "Existing",
                sourceId,
                currentScanUtc,
                @"C:\Archive")
        ]);

        var removedFolderCount =
            await repository.CompleteScanAsync(
                sourceId,
                currentScanUtc,
                []);

        Assert.Equal(1, removedFolderCount);

        var sourceFolders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        Assert.Equal(2, sourceFolders.Count);

        Assert.Contains(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive");

        Assert.Contains(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive\Existing");

        Assert.DoesNotContain(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive\Removed");

        Assert.Single(
            await repository.GetByRootSourceIdAsync(
                otherSourceId));
    }

    [Fact]
    public async Task CompleteScanAsync_ProtectsUnavailableBranchAndItsChildren()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();

        var previousScanUtc =
            new DateTimeOffset(
                2026,
                7,
                27,
                9,
                0,
                0,
                TimeSpan.Zero);

        var currentScanUtc =
            previousScanUtc.AddHours(1);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId,
                previousScanUtc),

            CreateFolder(
                @"C:\Archive\Unavailable",
                "Unavailable",
                sourceId,
                previousScanUtc,
                @"C:\Archive"),

            CreateFolder(
                @"C:\Archive\Unavailable\Previous child",
                "Previous child",
                sourceId,
                previousScanUtc,
                @"C:\Archive\Unavailable"),

            CreateFolder(
                @"C:\Archive\Removed",
                "Removed",
                sourceId,
                previousScanUtc,
                @"C:\Archive")
        ]);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId,
                currentScanUtc),

            CreateFolder(
                @"C:\Archive\Unavailable",
                "Unavailable",
                sourceId,
                currentScanUtc,
                @"C:\Archive",
                isAvailable: false)
        ]);

        var removedFolderCount =
            await repository.CompleteScanAsync(
                sourceId,
                currentScanUtc,
                [@"C:\Archive\Unavailable"]);

        Assert.Equal(1, removedFolderCount);

        var sourceFolders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        Assert.Equal(3, sourceFolders.Count);

        Assert.Contains(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive\Unavailable");

        Assert.Contains(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive\Unavailable\Previous child");

        Assert.DoesNotContain(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"C:\Archive\Removed");
    }

    [Fact]
    public async Task CompleteScanAsync_WhenRootIsUnavailable_PreservesWholeIndex()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();

        var previousScanUtc =
            new DateTimeOffset(
                2026,
                7,
                27,
                9,
                0,
                0,
                TimeSpan.Zero);

        var currentScanUtc =
            previousScanUtc.AddHours(1);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"\\server\archive",
                "archive",
                sourceId,
                previousScanUtc),

            CreateFolder(
                @"\\server\archive\Existing",
                "Existing",
                sourceId,
                previousScanUtc,
                @"\\server\archive")
        ]);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"\\server\archive",
                "archive",
                sourceId,
                currentScanUtc,
                isAvailable: false)
        ]);

        var removedFolderCount =
            await repository.CompleteScanAsync(
                sourceId,
                currentScanUtc,
                [@"\\server\archive"]);

        Assert.Equal(0, removedFolderCount);

        var sourceFolders =
            await repository.GetByRootSourceIdAsync(
                sourceId);

        Assert.Equal(2, sourceFolders.Count);

        Assert.Contains(
            sourceFolders,
            folder =>
                folder.FullPath ==
                @"\\server\archive\Existing");
    }

    [Fact]
    public async Task CompleteScanAsync_WhenCancelled_DoesNotDeleteFolders()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();

        var previousScanUtc =
            new DateTimeOffset(
                2026,
                7,
                27,
                9,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFolder(
                @"C:\Archive",
                "Archive",
                sourceId,
                previousScanUtc)
        ]);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
            () => repository.CompleteScanAsync(
                sourceId,
                previousScanUtc.AddHours(1),
                [],
                cancellationSource.Token));

        Assert.Single(
            await repository.GetByRootSourceIdAsync(
                sourceId));
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
        var dataDirectoryProvider =
            new TestApplicationDataDirectoryProvider(
                _temporaryDirectory);

        var pathProvider =
            new IndexDatabasePathProvider(
                dataDirectoryProvider);

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
        DateTimeOffset lastSeenUtc,
        string? parentFullPath = null,
        bool isAvailable = true)
    {
        var normalizedName =
            name
                .Trim()
                .ToLowerInvariant()
                .Replace('ё', 'е');

        return new FolderIndexUpsertItem(
            FullPath: fullPath,
            Name: name,
            NormalizedName: normalizedName,
            SearchTokens: normalizedName,
            SearchStems: string.Empty,
            ParentFullPath: parentFullPath,
            RootSourceId: rootSourceId,
            IsAvailable: isAvailable,
            LastSeenUtc: lastSeenUtc,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
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
