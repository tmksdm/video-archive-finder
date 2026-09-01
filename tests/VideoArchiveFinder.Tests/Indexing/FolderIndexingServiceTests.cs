using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Domain.ArchiveSources;
using VideoArchiveFinder.Infrastructure.Indexing;


namespace VideoArchiveFinder.Tests.Indexing;

public sealed class FolderIndexingServiceTests
{
    [Fact]
    public async Task ScanAsync_IndexesFoldersAndContinuesAfterError()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var accessException =
            new UnauthorizedAccessException(
                "Access denied.");

        var enumerator =
            new TestFolderTreeEnumerator(
            [
                new DiscoveredFolder(
                    FullPath: @"C:\Archive",
                    Name: "Archive",
                    ParentFullPath: null,
                    DirectSubfolderCount: 2,
                    IsAvailable: true,
                    IsReparsePoint: false),

                new FolderEnumerationError(
                    DirectoryPath:
                        @"C:\Archive\Закрытая папка",
                    Exception:
                        accessException),

                new DiscoveredFolder(
                    FullPath:
                        @"C:\Archive\ЁЖ_Дорога",
                    Name:
                        "ЁЖ_Дорога",
                    ParentFullPath:
                        @"C:\Archive",
                    DirectSubfolderCount: 0,
                    IsAvailable: true,
                    IsReparsePoint: false)
            ]);

        var repository =
            new RecordingFolderIndexRepository();

        var stateRepository =
            new RecordingFolderIndexingStateRepository();

        var progress =
            new RecordingProgress();

        var service =
            CreateService(
                enumerator,
                repository,
                stateRepository);


        var result =
            await service.ScanAsync(
                source,
                progress);

        Assert.Equal(source.Id, result.RootSourceId);
        Assert.Equal(2, result.DiscoveredFolderCount);
        Assert.Equal(2, result.IndexedFolderCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.True(
            result.CompletedAtUtc >=
            result.StartedAtUtc);

        var savedState =
            Assert.Single(
                stateRepository.SavedStates);

        Assert.Equal(
            result.RootSourceId,
            savedState.RootSourceId);

        Assert.Equal(
            result.DiscoveredFolderCount,
            savedState.DiscoveredFolderCount);

        Assert.Equal(
            result.IndexedFolderCount,
            savedState.IndexedFolderCount);

        Assert.Equal(
            result.ErrorCount,
            savedState.ErrorCount);

        Assert.Equal(
            result.StartedAtUtc,
            savedState.StartedAtUtc);

        Assert.Equal(
            result.CompletedAtUtc,
            savedState.CompletedAtUtc);


        var batch =
            Assert.Single(repository.Batches);

        Assert.Equal(2, batch.Count);

        var indexedFolder =
            Assert.Single(
                batch,
                item => item.FullPath ==
                    @"C:\Archive\ЁЖ_Дорога");

        Assert.Equal(
            "еж_дорога",
            indexedFolder.NormalizedName);

        Assert.Equal(
            "еж дорога",
            indexedFolder.SearchTokens);

        Assert.Equal(
            "еж дорог",
            indexedFolder.SearchStems);


        Assert.Equal(
            @"C:\Archive",
            indexedFolder.ParentFullPath);

        Assert.Equal(
            source.Id,
            indexedFolder.RootSourceId);

        Assert.True(indexedFolder.IsAvailable);
        Assert.Equal(
            0,
            indexedFolder.DirectVideoFileCount);

        Assert.Contains(
            progress.Reports,
            report =>
                report.ErrorCount == 1);

        Assert.Equal(
            FolderIndexingStage.Completed,
            progress.Reports[^1].Stage);

        var completion =
            Assert.Single(repository.Completions);

        Assert.Equal(source.Id, completion.RootSourceId);
        Assert.Contains(
            @"C:\Archive\Закрытая папка",
            completion.ProtectedPaths);
    }

    [Fact]
    public async Task ScanAsync_IndexesDiscoveredVideoFiles()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var folderPath =
            @"C:\Archive\Видео";

        var lastWriteTimeUtc =
            new DateTimeOffset(
                2026,
                8,
                1,
                10,
                0,
                0,
                TimeSpan.Zero);

        var enumerator =
            new TestFolderTreeEnumerator(
            [
                new DiscoveredFolder(
                    FullPath:
                        folderPath,
                    Name:
                        "Видео",
                    ParentFullPath:
                        @"C:\Archive",
                    DirectSubfolderCount:
                        0,
                    IsAvailable:
                        true,
                    IsReparsePoint:
                        false)
            ]);

        var discoveryService =
            new TestVideoFileDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files:
                    [
                        new DiscoveredVideoFile(
                            FullPath:
                                folderPath +
                                @"\ЁЖ_Дорога.MP4",
                            Name:
                                "ЁЖ_Дорога.MP4",
                            Extension:
                                ".mp4",
                            SizeBytes:
                                12_345,
                            LastWriteTimeUtc:
                                lastWriteTimeUtc)
                    ],
                    ErrorCount:
                        0,
                    CanRemoveStaleEntries:
                        true));

        var folderRepository =
            new RecordingFolderIndexRepository();

        var videoRepository =
            new RecordingVideoFileIndexRepository();

        var analysisQueue =
            new RecordingVideoFileAnalysisQueue(
                (_, _, _) =>
                {
                    Assert.Single(
                        videoRepository.Batches);

                    return ValueTask.CompletedTask;
                });

        var service =
            CreateService(
                enumerator,
                folderRepository,
                videoFileDiscoveryService:
                    discoveryService,
                videoFileIndexRepository:
                    videoRepository,
                videoFileAnalysisQueue:
                    analysisQueue);



        var result =
            await service.ScanAsync(source);

        Assert.Equal(0, result.ErrorCount);

        Assert.Equal(
            folderPath,
            Assert.Single(
                discoveryService
                    .RequestedFolderPaths));

        var folderBatch =
            Assert.Single(
                folderRepository.Batches);

        var indexedFolder =
            Assert.Single(folderBatch);

        Assert.Equal(
            1,
            indexedFolder.DirectVideoFileCount);

        var videoBatch =
            Assert.Single(
                videoRepository.Batches);

        var indexedVideo =
            Assert.Single(videoBatch);

        Assert.Equal(
            folderPath + @"\ЁЖ_Дорога.MP4",
            indexedVideo.FullPath);

        Assert.Equal(
            "ЁЖ_Дорога.MP4",
            indexedVideo.Name);

        Assert.Equal(
            "еж_дорога.mp4",
            indexedVideo.NormalizedName);

        Assert.Equal(
            ".mp4",
            indexedVideo.Extension);

        Assert.Equal(
            12_345,
            indexedVideo.SizeBytes);

        Assert.Equal(
            lastWriteTimeUtc,
            indexedVideo.LastWriteTimeUtc);

        Assert.Equal(
            folderPath,
            indexedVideo.FolderFullPath);

        Assert.Equal(
            source.Id,
            indexedVideo.RootSourceId);

        Assert.True(indexedVideo.IsAvailable);

        Assert.Equal(
            result.StartedAtUtc,
            indexedVideo.LastSeenUtc);

        var completion =
            Assert.Single(
                videoRepository.Completions);

        var analysisRequest =
            Assert.Single(
                analysisQueue.Requests);

        Assert.Equal(
            source.Id,
            analysisRequest.RootSourceId);

        Assert.Equal(
            folderPath + @"\ЁЖ_Дорога.MP4",
            analysisRequest.FullPath);

        Assert.Equal(
            source.Id,
            completion.RootSourceId);

        Assert.Equal(
            folderPath,
            completion.FolderFullPath);

        Assert.Equal(
            result.StartedAtUtc,
            completion.ScanStartedAtUtc);
    }

    [Fact]
    public async Task ScanAsync_WhenVideoIndexingIsDisabled_SkipsFiles()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var folderPath =
            @"C:\Archive\Видео";

        var discoveryService =
            new TestVideoFileDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files:
                    [
                        new DiscoveredVideoFile(
                            FullPath:
                                folderPath + @"\Клип.mp4",
                            Name: "Клип.mp4",
                            Extension: ".mp4",
                            SizeBytes: 1_000,
                            LastWriteTimeUtc:
                                DateTimeOffset.UtcNow)
                    ],
                    ErrorCount: 0,
                    CanRemoveStaleEntries: true));

        var folderRepository =
            new RecordingFolderIndexRepository();

        var videoRepository =
            new RecordingVideoFileIndexRepository();

        var service =
            CreateService(
                new TestFolderTreeEnumerator(
                [
                    new DiscoveredFolder(
                        FullPath: folderPath,
                        Name: "Видео",
                        ParentFullPath: @"C:\Archive",
                        DirectSubfolderCount: 0,
                        IsAvailable: true,
                        IsReparsePoint: false)
                ]),
                folderRepository,
                videoFileDiscoveryService:
                    discoveryService,
                videoFileIndexRepository:
                    videoRepository,
                indexVideoFilesDuringFolderScan: false);

        await service.ScanAsync(source);

        Assert.Empty(
            discoveryService.RequestedFolderPaths);

        Assert.Empty(videoRepository.Batches);
        Assert.Empty(videoRepository.Completions);

        var indexedFolder =
            Assert.Single(
                Assert.Single(
                    folderRepository.Batches));

        Assert.Equal(
            0,
            indexedFolder.DirectVideoFileCount);
    }

    [Fact]
    public async Task ScanAsync_WhenVideoDiscoveryIsIncomplete_DoesNotRemoveStaleFiles()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var folderPath =
            @"C:\Archive\Повреждённая папка";

        var enumerator =
            new TestFolderTreeEnumerator(
            [
                new DiscoveredFolder(
                    FullPath:
                        folderPath,
                    Name:
                        "Повреждённая папка",
                    ParentFullPath:
                        @"C:\Archive",
                    DirectSubfolderCount:
                        0,
                    IsAvailable:
                        true,
                    IsReparsePoint:
                        false)
            ]);

        var discoveryService =
            new TestVideoFileDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files:
                    [
                        new DiscoveredVideoFile(
                            FullPath:
                                folderPath +
                                @"\Рабочий.mkv",
                            Name:
                                "Рабочий.mkv",
                            Extension:
                                ".mkv",
                            SizeBytes:
                                5_000,
                            LastWriteTimeUtc:
                                new DateTimeOffset(
                                    2026,
                                    8,
                                    1,
                                    11,
                                    0,
                                    0,
                                    TimeSpan.Zero))
                    ],
                    ErrorCount:
                        1,
                    CanRemoveStaleEntries:
                        false));

        var folderRepository =
            new RecordingFolderIndexRepository();

        var videoRepository =
            new RecordingVideoFileIndexRepository();

        var service =
            CreateService(
                enumerator,
                folderRepository,
                videoFileDiscoveryService:
                    discoveryService,
                videoFileIndexRepository:
                    videoRepository);

        var result =
            await service.ScanAsync(source);

        Assert.Equal(1, result.ErrorCount);

        var folderBatch =
            Assert.Single(
                folderRepository.Batches);

        var indexedFolder =
            Assert.Single(folderBatch);

        Assert.Equal(
            1,
            indexedFolder.DirectVideoFileCount);

        var videoBatch =
            Assert.Single(
                videoRepository.Batches);

        var indexedVideo =
            Assert.Single(videoBatch);

        Assert.Equal(
            "Рабочий.mkv",
            indexedVideo.Name);

        Assert.Empty(
            videoRepository.Completions);
    }


    [Fact]
    public async Task ScanAsync_WhenAnalysisQueueFails_ContinuesIndexing()
    {
        var scenario =
            CreateSingleVideoScenario();

        var folderRepository =
            new RecordingFolderIndexRepository();

        var analysisQueue =
            new RecordingVideoFileAnalysisQueue(
                (_, _, _) =>
                    ValueTask.FromException(
                        new InvalidOperationException(
                            "Test queue failure.")));

        var service =
            CreateService(
                scenario.Enumerator,
                folderRepository,
                videoFileDiscoveryService:
                    scenario.DiscoveryService,
                videoFileAnalysisQueue:
                    analysisQueue);

        var result =
            await service.ScanAsync(
                scenario.Source);

        Assert.Equal(1, result.ErrorCount);
        Assert.Single(folderRepository.Batches);
        Assert.Single(analysisQueue.Requests);
    }

    [Fact]
    public async Task ScanAsync_WhenAnalysisQueueIsCancelled_CancelsScan()
    {
        var scenario =
            CreateSingleVideoScenario();

        var folderRepository =
            new RecordingFolderIndexRepository();

        var enqueueStarted =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        var analysisQueue =
            new RecordingVideoFileAnalysisQueue(
                async (_, _, cancellationToken) =>
                {
                    enqueueStarted.TrySetResult();

                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                });

        var service =
            CreateService(
                scenario.Enumerator,
                folderRepository,
                videoFileDiscoveryService:
                    scenario.DiscoveryService,
                videoFileAnalysisQueue:
                    analysisQueue);

        using var cancellationSource =
            new CancellationTokenSource();

        var scanningTask =
            service.ScanAsync(
                scenario.Source,
                cancellationToken:
                    cancellationSource.Token);

        await enqueueStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
                () => scanningTask);

        Assert.Single(analysisQueue.Requests);
        Assert.Empty(folderRepository.Batches);
    }



    [Fact]
    public async Task ScanAsync_MoreThanBatchSize_WritesSeveralBatches()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var entries =
            Enumerable.Range(0, 251)
                .Select(
                    index =>
                        (FolderEnumerationEntry)
                        new DiscoveredFolder(
                            FullPath:
                                $@"C:\Archive\Folder{index}",
                            Name:
                                $"Folder{index}",
                            ParentFullPath:
                                @"C:\Archive",
                            DirectSubfolderCount: 0,
                            IsAvailable: true,
                            IsReparsePoint: false))
                .ToArray();

        var repository =
            new RecordingFolderIndexRepository();

        var service =
            CreateService(
                new TestFolderTreeEnumerator(
                    entries),
                repository);

        var result =
            await service.ScanAsync(source);

        Assert.Equal(251, result.IndexedFolderCount);
        Assert.Equal(2, repository.Batches.Count);
        Assert.Equal(250, repository.Batches[0].Count);
        Assert.Single(repository.Batches[1]);
        Assert.Single(repository.Completions);
    }

    [Fact]
    public async Task ScanAsync_WhenCancelled_ThrowsCancellation()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var repository =
            new RecordingFolderIndexRepository();

        var stateRepository =
            new RecordingFolderIndexingStateRepository();

        var service =
            CreateService(
                new BlockingFolderTreeEnumerator(),
                repository,
                stateRepository);


        using var cancellationSource =
            new CancellationTokenSource();

        var scanningTask =
            service.ScanAsync(
                source,
                cancellationToken:
                    cancellationSource.Token);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
                () => scanningTask);

        Assert.Empty(repository.Batches);
        Assert.Empty(repository.Completions);
        Assert.Empty(stateRepository.SavedStates);
    }

    private static SingleVideoScenario
        CreateSingleVideoScenario()
    {
        var source =
            ArchiveSource.Create(
                @"C:\Archive");

        var folderPath =
            @"C:\Archive\Видео";

        var enumerator =
            new TestFolderTreeEnumerator(
            [
                new DiscoveredFolder(
                FullPath:
                    folderPath,
                Name:
                    "Видео",
                ParentFullPath:
                    @"C:\Archive",
                DirectSubfolderCount:
                    0,
                IsAvailable:
                    true,
                IsReparsePoint:
                    false)
            ]);

        var discoveryService =
            new TestVideoFileDiscoveryService(
                new VideoFileDiscoveryResult(
                    Files:
                    [
                        new DiscoveredVideoFile(
                        FullPath:
                            folderPath +
                            @"\Видео.mp4",
                        Name:
                            "Видео.mp4",
                        Extension:
                            ".mp4",
                        SizeBytes:
                            1_000,
                        LastWriteTimeUtc:
                            new DateTimeOffset(
                                2026,
                                8,
                                1,
                                12,
                                0,
                                0,
                                TimeSpan.Zero))
                    ],
                    ErrorCount:
                        0,
                    CanRemoveStaleEntries:
                        true));

        return new SingleVideoScenario(
            source,
            enumerator,
            discoveryService);
    }

    private sealed record SingleVideoScenario(
        ArchiveSource Source,
        TestFolderTreeEnumerator Enumerator,
        TestVideoFileDiscoveryService DiscoveryService);


    private static FolderIndexingService CreateService(
        IFolderTreeEnumerator enumerator,
        IFolderIndexRepository repository,
        IFolderIndexingStateRepository?
            stateRepository = null,
        IVideoFileDiscoveryService?
            videoFileDiscoveryService = null,
        IVideoFileIndexRepository?
            videoFileIndexRepository = null,
        IVideoFileAnalysisQueue?
            videoFileAnalysisQueue = null,
        bool indexVideoFilesDuringFolderScan = true)

    {
        return new FolderIndexingService(
            enumerator,
            repository,
            videoFileDiscoveryService ??
                new EmptyVideoFileDiscoveryService(),
            videoFileIndexRepository ??
                new RecordingVideoFileIndexRepository(),
            videoFileAnalysisQueue ??
                new RecordingVideoFileAnalysisQueue(),
            stateRepository ??
                new RecordingFolderIndexingStateRepository(),
            new TextNormalizationService(),
            new RussianSearchStemService(),
            NullLogger<FolderIndexingService>.Instance,
            indexVideoFilesDuringFolderScan);

    }




    private sealed class TestFolderTreeEnumerator
        : IFolderTreeEnumerator
    {
        private readonly
            IReadOnlyList<FolderEnumerationEntry>
            _entries;

        public TestFolderTreeEnumerator(
            IReadOnlyList<FolderEnumerationEntry> entries)
        {
            _entries = entries;
        }

        public async IAsyncEnumerable<
            FolderEnumerationEntry> EnumerateAsync(
                string rootPath,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            foreach (var entry in _entries)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                await Task.Yield();

                yield return entry;
            }
        }
    }

    private sealed class BlockingFolderTreeEnumerator
        : IFolderTreeEnumerator
    {
        public async IAsyncEnumerable<
            FolderEnumerationEntry> EnumerateAsync(
                string rootPath,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            yield break;
        }
    }

    private sealed class RecordingFolderIndexRepository
        : IFolderIndexRepository
    {
        public List<
            IReadOnlyList<FolderIndexUpsertItem>>
            Batches
        { get; } = [];

        public List<ScanCompletion>
            Completions
        { get; } = [];

        public Task UpsertBatchAsync(
            IReadOnlyCollection<
                FolderIndexUpsertItem> folders,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Batches.Add(folders.ToArray());

            return Task.CompletedTask;
        }

        public Task<int> CompleteScanAsync(
            Guid rootSourceId,
            DateTimeOffset scanStartedAtUtc,
            IReadOnlyCollection<string> protectedPaths,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Completions.Add(
                new ScanCompletion(
                    rootSourceId,
                    scanStartedAtUtc,
                    protectedPaths.ToArray()));

            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<IndexedFolder>>
            GetByRootSourceIdAsync(
                Guid rootSourceId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<IndexedFolder>>([]);
        }

        public Task<IReadOnlyList<IndexedFolder>>
            GetChildrenAsync(
                long parentFolderId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<IndexedFolder>>([]);
        }
    }

    private sealed record ScanCompletion(
        Guid RootSourceId,
        DateTimeOffset ScanStartedAtUtc,
        IReadOnlyList<string> ProtectedPaths);

    private sealed class RecordingFolderIndexingStateRepository
        : IFolderIndexingStateRepository
    {
        public List<FolderIndexingState> SavedStates
        {
            get;
        } = [];

        public Task SaveAsync(
            FolderIndexingState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            SavedStates.Add(state);

            return Task.CompletedTask;
        }

        public Task<FolderIndexingState?> GetAsync(
            Guid rootSourceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            FolderIndexingState? state =
                SavedStates.LastOrDefault(
                    item =>
                        item.RootSourceId ==
                        rootSourceId);

            return Task.FromResult(state);
        }
    }

    private sealed class TestVideoFileDiscoveryService
        : IVideoFileDiscoveryService
    {
        private readonly VideoFileDiscoveryResult
            _result;

        public TestVideoFileDiscoveryService(
            VideoFileDiscoveryResult result)
        {
            _result = result;
        }

        public List<string> RequestedFolderPaths
        {
            get;
        } = [];

        public Task<VideoFileDiscoveryResult> DiscoverAsync(
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            RequestedFolderPaths.Add(folderPath);

            return Task.FromResult(_result);
        }
    }

    private sealed class EmptyVideoFileDiscoveryService
        : IVideoFileDiscoveryService
    {
        public Task<VideoFileDiscoveryResult> DiscoverAsync(
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult(
                new VideoFileDiscoveryResult(
                    Files: [],
                    ErrorCount: 0,
                    CanRemoveStaleEntries: true));
        }
    }

    private sealed class RecordingVideoFileIndexRepository
        : IVideoFileIndexRepository
    {
        public List<
            IReadOnlyList<VideoFileIndexUpsertItem>>
            Batches
        {
            get;
        } = [];

        public List<VideoFolderScanCompletion>
            Completions
        {
            get;
        } = [];

        public Task UpsertBatchAsync(
            IReadOnlyCollection<
                VideoFileIndexUpsertItem> files,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Batches.Add(files.ToArray());

            return Task.CompletedTask;
        }

        public Task<int> CompleteFolderScanAsync(
            Guid rootSourceId,
            string folderFullPath,
            DateTimeOffset scanStartedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Completions.Add(
                new VideoFolderScanCompletion(
                    rootSourceId,
                    folderFullPath,
                    scanStartedAtUtc));

            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<IndexedVideoFile>>
            GetByFolderPathAsync(
                Guid rootSourceId,
                string folderFullPath,
                CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult<
                IReadOnlyList<IndexedVideoFile>>([]);
        }

        public Task<bool> UpdateAnalysisAsync(
            VideoFileAnalysisUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> UpdateThumbnailAsync(
            VideoFileThumbnailUpdate update,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(update);

            return Task.FromResult(false);
        }



    }

    private sealed record VideoFolderScanCompletion(
        Guid RootSourceId,
        string FolderFullPath,
        DateTimeOffset ScanStartedAtUtc);



    private sealed class RecordingProgress
        : IProgress<FolderIndexingProgress>
    {
        public List<FolderIndexingProgress>
            Reports
        { get; } = [];

        public void Report(
            FolderIndexingProgress value)
        {
            Reports.Add(value);
        }
    }

    private sealed class RecordingVideoFileAnalysisQueue
        : IVideoFileAnalysisQueue
    {
        private readonly Func<
            Guid,
            string,
            CancellationToken,
            ValueTask> _enqueue;

        public RecordingVideoFileAnalysisQueue(
            Func<
                Guid,
                string,
                CancellationToken,
                ValueTask>? enqueue = null)
        {
            _enqueue =
                enqueue ??
                ((_, _, _) =>
                    ValueTask.CompletedTask);
        }

        public List<VideoAnalysisQueueRequest>
            Requests
        { get; } = [];

        public ValueTask EnqueueAsync(
            VideoFileAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken
                .ThrowIfCancellationRequested();

            Requests.Add(
                new VideoAnalysisQueueRequest(
                    request.RootSourceId,
                    request.FullPath));

            return _enqueue(
                request.RootSourceId,
                request.FullPath,
                cancellationToken);
        }

    }


    private sealed record VideoAnalysisQueueRequest(
        Guid RootSourceId,
        string FullPath);


}
