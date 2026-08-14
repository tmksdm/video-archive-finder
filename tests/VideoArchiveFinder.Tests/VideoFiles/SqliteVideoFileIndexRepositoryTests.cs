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

    [Fact]
    public async Task UpdateAnalysisAsync_StoresSuccessfulMetadata()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var folderPath =
            @"C:\Archive\Folder";
        var videoPath =
            @"C:\Archive\Folder\Video.mp4";

        await repository.UpsertBatchAsync(
        [
            CreateFile(
            fullPath: videoPath,
            name: "Video.mp4",
            folderFullPath: folderPath,
            rootSourceId: sourceId)
        ]);

        var duration =
            TimeSpan.FromSeconds(125.5);

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: sourceId,
                    FullPath: videoPath,
                    State:
                        VideoFileAnalysisState.Succeeded,
                    HasVideoStream: true,
                    Duration: duration,
                    Width: 1920,
                    Height: 1080,
                    Codec: " h264 "));

        Assert.True(wasUpdated);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                folderPath);

        var file = Assert.Single(files);

        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            file.AnalysisState);

        Assert.Equal(
            true,
            file.HasVideoStream);

        Assert.Equal(
            duration,
            file.Duration);

        Assert.Equal(
            1920,
            file.Width);

        Assert.Equal(
            1080,
            file.Height);

        Assert.Equal(
            "h264",
            file.Codec);
    }

    [Fact]
    public async Task UpdateAnalysisAsync_StoresSuccessfulResultWithoutVideoStream()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var folderPath =
            @"C:\Archive\Folder";
        var videoPath =
            @"C:\Archive\Folder\AudioOnly.mp4";

        await repository.UpsertBatchAsync(
        [
            CreateFile(
            fullPath: videoPath,
            name: "AudioOnly.mp4",
            folderFullPath: folderPath,
            rootSourceId: sourceId)
        ]);

        var duration =
            TimeSpan.FromSeconds(60);

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: sourceId,
                    FullPath: videoPath,
                    State:
                        VideoFileAnalysisState.Succeeded,
                    HasVideoStream: false,
                    Duration: duration,
                    Width: null,
                    Height: null,
                    Codec: null));

        Assert.True(wasUpdated);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                folderPath);

        var file = Assert.Single(files);

        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            file.AnalysisState);

        Assert.Equal(
            false,
            file.HasVideoStream);

        Assert.Equal(
            duration,
            file.Duration);

        Assert.Null(file.Width);
        Assert.Null(file.Height);
        Assert.Null(file.Codec);
    }

    [Fact]
    public async Task UpdateAnalysisAsync_StoresFailedStateWithoutMetadata()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var folderPath =
            @"C:\Archive\Folder";
        var videoPath =
            @"C:\Archive\Folder\Broken.mp4";

        await repository.UpsertBatchAsync(
        [
            CreateFile(
            fullPath: videoPath,
            name: "Broken.mp4",
            folderFullPath: folderPath,
            rootSourceId: sourceId)
        ]);

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: sourceId,
                    FullPath: videoPath,
                    State:
                        VideoFileAnalysisState.Failed,
                    HasVideoStream: null,
                    Duration: null,
                    Width: null,
                    Height: null,
                    Codec: null));

        Assert.True(wasUpdated);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                folderPath);

        var file = Assert.Single(files);

        Assert.Equal(
            VideoFileAnalysisState.Failed,
            file.AnalysisState);

        Assert.Null(file.HasVideoStream);
        Assert.Null(file.Duration);
        Assert.Null(file.Width);
        Assert.Null(file.Height);
        Assert.Null(file.Codec);
    }


    [Fact]
    public async Task UpsertBatchAsync_WhenFileIsUnchanged_PreservesAnalysis()
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var folderPath =
            @"C:\Archive\Folder";
        var videoPath =
            @"C:\Archive\Folder\Video.mp4";

        var lastWriteTimeUtc =
            new DateTimeOffset(
                2026,
                8,
                1,
                10,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: videoPath,
                name: "Video.mp4",
                folderFullPath: folderPath,
                rootSourceId: sourceId,
                sizeBytes: 10_000,
                lastWriteTimeUtc: lastWriteTimeUtc)
        ]);

        var duration =
            TimeSpan.FromSeconds(125.5);

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: sourceId,
                    FullPath: videoPath,
                    State:
                        VideoFileAnalysisState.Succeeded,
                    HasVideoStream: true,
                    Duration: duration,
                    Width: 1920,
                    Height: 1080,
                    Codec: "h264"));

        Assert.True(wasUpdated);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: videoPath,
                name: "VIDEO.MP4",
                normalizedName: "video.mp4",
                extension: ".MP4",
                folderFullPath: folderPath,
                rootSourceId: sourceId,
                sizeBytes: 10_000,
                lastWriteTimeUtc: lastWriteTimeUtc,
                isAvailable: true,
                lastSeenUtc:
                    new DateTimeOffset(
                        2026,
                        8,
                        2,
                        12,
                        0,
                        0,
                        TimeSpan.Zero))
        ]);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                folderPath);

        var file = Assert.Single(files);

        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            file.AnalysisState);

        Assert.Equal(true, file.HasVideoStream);
        Assert.Equal(duration, file.Duration);
        Assert.Equal(1920, file.Width);
        Assert.Equal(1080, file.Height);
        Assert.Equal("h264", file.Codec);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UpsertBatchAsync_WhenFileChanges_ResetsAnalysis(
        bool changeSize,
        bool changeLastWriteTime)
    {
        var repository = await CreateRepositoryAsync();

        var sourceId = Guid.NewGuid();
        var folderPath =
            @"C:\Archive\Folder";
        var videoPath =
            @"C:\Archive\Folder\Video.mp4";

        var originalWriteTimeUtc =
            new DateTimeOffset(
                2026,
                8,
                1,
                10,
                0,
                0,
                TimeSpan.Zero);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: videoPath,
                name: "Video.mp4",
                folderFullPath: folderPath,
                rootSourceId: sourceId,
                sizeBytes: 10_000,
                lastWriteTimeUtc:
                    originalWriteTimeUtc)
        ]);

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: sourceId,
                    FullPath: videoPath,
                    State:
                        VideoFileAnalysisState.Succeeded,
                    HasVideoStream: true,
                    Duration:
                        TimeSpan.FromSeconds(90),
                    Width: 1920,
                    Height: 1080,
                    Codec: "h264"));

        Assert.True(wasUpdated);

        await repository.UpsertBatchAsync(
        [
            CreateFile(
                fullPath: videoPath,
                name: "Video.mp4",
                folderFullPath: folderPath,
                rootSourceId: sourceId,
                sizeBytes:
                    changeSize
                        ? 20_000
                        : 10_000,
                lastWriteTimeUtc:
                    changeLastWriteTime
                        ? originalWriteTimeUtc.AddMinutes(1)
                        : originalWriteTimeUtc)
        ]);

        var files =
            await repository.GetByFolderPathAsync(
                sourceId,
                folderPath);

        var file = Assert.Single(files);

        Assert.Equal(
            VideoFileAnalysisState.NotAnalyzed,
            file.AnalysisState);

        Assert.Null(file.HasVideoStream);
        Assert.Null(file.Duration);
        Assert.Null(file.Width);
        Assert.Null(file.Height);
        Assert.Null(file.Codec);
    }

    [Fact]
    public async Task UpdateAnalysisAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        var repository = await CreateRepositoryAsync();

        var wasUpdated =
            await repository.UpdateAnalysisAsync(
                new VideoFileAnalysisUpdate(
                    RootSourceId: Guid.NewGuid(),
                    FullPath:
                        @"C:\Archive\Missing.mp4",
                    State:
                        VideoFileAnalysisState.Failed,
                    HasVideoStream: null,
                    Duration: null,
                    Width: null,
                    Height: null,
                    Codec: null));

        Assert.False(wasUpdated);
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
