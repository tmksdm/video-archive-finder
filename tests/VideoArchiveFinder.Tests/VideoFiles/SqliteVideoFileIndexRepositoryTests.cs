using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class SqliteVideoFileIndexRepositoryTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpsertBatchAsync_WritesAndReadsFilesByFolder()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var otherSourceId = Guid.NewGuid();

        var lastWriteTimeUtc =
            new DateTimeOffset(
                2026,
                7,
                30,
                10,
                0,
                0,
                TimeSpan.Zero);

        var lastSeenUtc =
            new DateTimeOffset(
                2026,
                7,
                30,
                12,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath:
                    @"C:\Archive\Folder\Second.MOV",
                name: "Second.MOV",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                sizeBytes: 2_000,
                lastWriteTimeUtc: lastWriteTimeUtc,
                lastSeenUtc: lastSeenUtc),

            CreateFile(
                fullPath:
                    @"C:\Archive\Folder\First.mp4",
                name: "First.mp4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                sizeBytes: 1_000,
                lastWriteTimeUtc: lastWriteTimeUtc,
                lastSeenUtc: lastSeenUtc),

            CreateFile(
                fullPath:
                    @"C:\Archive\Other\Other.mkv",
                name: "Other.mkv",
                folderFullPath:
                    @"C:\Archive\Other",
                rootSourceId: sourceId,
                lastSeenUtc: lastSeenUtc),

            CreateFile(
                fullPath:
                    @"C:\Archive\Folder\Foreign.mp4",
                name: "Foreign.mp4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: otherSourceId,
                lastSeenUtc: lastSeenUtc)
        ]);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                @"C:\Archive\Folder");

        Assert.Equal(2, files.Count);

        Assert.Equal(
            "First.mp4",
            files[0].Name);

        Assert.Equal(
            "Second.MOV",
            files[1].Name);

        var firstFile = files[0];

        Assert.True(firstFile.Id > 0);

        Assert.Equal(
            @"C:\Archive\Folder\First.mp4",
            firstFile.FullPath);

        Assert.Equal(
            "first.mp4",
            firstFile.NormalizedName);

        Assert.Equal(
            ".mp4",
            firstFile.Extension);

        Assert.Equal(
            1_000,
            firstFile.SizeBytes);

        Assert.Equal(
            lastWriteTimeUtc,
            firstFile.LastWriteTimeUtc);

        Assert.Equal(
            @"C:\Archive\Folder",
            firstFile.FolderFullPath);

        Assert.Equal(
            sourceId,
            firstFile.RootSourceId);

        Assert.True(firstFile.IsAvailable);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenFileExists_UpdatesWithoutDuplicate()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var fullPath =
            @"C:\Archive\Folder\Video.mp4";

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: fullPath,
                name: "Video.mp4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                sizeBytes: 1_000)
        ]);

        var original = Assert.Single(
            await repository.GetByFolderPathAsync(
                sourceId,
                @"C:\Archive\Folder"));

        var updatedWriteTimeUtc =
            new DateTimeOffset(
                2026,
                7,
                31,
                9,
                30,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: fullPath,
                name: "VIDEO.MP4",
                normalizedName: "video.mp4",
                extension: ".MP4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                sizeBytes: 5_000,
                lastWriteTimeUtc:
                    updatedWriteTimeUtc,
                isAvailable: false)
        ]);

        var updated = Assert.Single(
            await repository.GetByFolderPathAsync(
                sourceId,
                @"C:\Archive\Folder"));

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("VIDEO.MP4", updated.Name);
        Assert.Equal(".MP4", updated.Extension);
        Assert.Equal(5_000, updated.SizeBytes);

        Assert.Equal(
            updatedWriteTimeUtc,
            updated.LastWriteTimeUtc);

        Assert.False(updated.IsAvailable);
    }

    [Fact]
    public async Task CompleteFolderScanAsync_RemovesOnlyStaleFilesFromFolder()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();

        var scanStartedAtUtc =
            new DateTimeOffset(
                2026,
                7,
                31,
                12,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath:
                    @"C:\Archive\Folder\Stale.mp4",
                name: "Stale.mp4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                lastSeenUtc:
                    scanStartedAtUtc.AddMinutes(-1)),

            CreateFile(
                fullPath:
                    @"C:\Archive\Folder\Current.mp4",
                name: "Current.mp4",
                folderFullPath:
                    @"C:\Archive\Folder",
                rootSourceId: sourceId,
                lastSeenUtc: scanStartedAtUtc),

            CreateFile(
                fullPath:
                    @"C:\Archive\Other\Protected.mp4",
                name: "Protected.mp4",
                folderFullPath:
                    @"C:\Archive\Other",
                rootSourceId: sourceId,
                lastSeenUtc:
                    scanStartedAtUtc.AddMinutes(-1))
        ]);

        var removedCount =
            await repository.CompleteFolderScanAsync(
                sourceId,
                @"C:\Archive\Folder",
                scanStartedAtUtc);

        Assert.Equal(1, removedCount);

        var scannedFolderFiles =
            await repository.GetByFolderPathAsync(
                sourceId,
                @"C:\Archive\Folder");

        var currentFile =
            Assert.Single(scannedFolderFiles);

        Assert.Equal(
            "Current.mp4",
            currentFile.Name);

        var otherFolderFiles =
            await repository.GetByFolderPathAsync(
                sourceId,
                @"C:\Archive\Other");

        var protectedFile =
            Assert.Single(otherFolderFiles);

        Assert.Equal(
            "Protected.mp4",
            protectedFile.Name);
    }

    private async Task<SqliteVideoFileIndexRepository>
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

        return new SqliteVideoFileIndexRepository(
            pathProvider,
            NullLogger<
                SqliteVideoFileIndexRepository>.Instance);
    }

    private static VideoFileIndexUpsertItem CreateFile(
        string fullPath,
        string name,
        string folderFullPath,
        Guid rootSourceId,
        string? normalizedName = null,
        string? extension = null,
        long sizeBytes = 0,
        DateTimeOffset? lastWriteTimeUtc = null,
        bool isAvailable = true,
        DateTimeOffset? lastSeenUtc = null)
    {
        return new VideoFileIndexUpsertItem(
            FullPath: fullPath,
            Name: name,
            NormalizedName:
                normalizedName ??
                name.ToLowerInvariant(),
            Extension:
                extension ??
                Path.GetExtension(fullPath),
            SizeBytes: sizeBytes,
            LastWriteTimeUtc:
                lastWriteTimeUtc ??
                new DateTimeOffset(
                    2026,
                    7,
                    30,
                    10,
                    0,
                    0,
                    TimeSpan.Zero),
            FolderFullPath:
                folderFullPath,
            RootSourceId:
                rootSourceId,
            IsAvailable:
                isAvailable,
            LastSeenUtc:
                lastSeenUtc ??
                new DateTimeOffset(
                    2026,
                    7,
                    30,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite
            .SqliteConnection
            .ClearAllPools();

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
