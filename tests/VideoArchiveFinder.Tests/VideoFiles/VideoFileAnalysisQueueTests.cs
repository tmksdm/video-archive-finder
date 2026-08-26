using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFileAnalysisQueueTests
{
    private static readonly Guid RootSourceId =
        Guid.Parse(
            "6072b141-a693-477e-af96-a797e5491c56");

    [Fact]
    public async Task Queue_DoesNotExceedConfiguredParallelism()
    {
        var twoWorkersStarted =
            CreateCompletionSource();

        var allFilesCompleted =
            CreateCompletionSource();

        var releaseWorkers =
            CreateCompletionSource();

        var runningCount = 0;
        var maximumRunningCount = 0;
        var startedCount = 0;
        var completedCount = 0;

        var analysisService =
            new TestVideoFileAnalysisService(
                async (_, _, cancellationToken) =>
                {
                    var running =
                        Interlocked.Increment(
                            ref runningCount);

                    UpdateMaximum(
                        ref maximumRunningCount,
                        running);

                    if (Interlocked.Increment(
                            ref startedCount) == 2)
                    {
                        twoWorkersStarted.TrySetResult();
                    }

                    try
                    {
                        await releaseWorkers.Task
                            .WaitAsync(cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(
                            ref runningCount);
                    }

                    if (Interlocked.Increment(
                            ref completedCount) == 4)
                    {
                        allFilesCompleted.TrySetResult();
                    }

                    return SuccessfulResult();
                });

        await using var queue =
            new VideoFileAnalysisQueue(
                analysisService,
                new TestStaticThumbnailGenerationQueue(),
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 2,
                capacity: 8);

        for (var index = 0; index < 4; index++)
        {
            await queue.EnqueueAsync(
                CreateRequest(
                $@"C:\Archive\Video{index}.mp4"));
        }

        await twoWorkersStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await Task.Delay(100);

        Assert.Equal(
            2,
            Volatile.Read(
                ref maximumRunningCount));

        releaseWorkers.TrySetResult();

        await allFilesCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsync_CancelsRunningAnalysis()
    {
        var analysisStarted =
            CreateCompletionSource();

        var analysisCancelled =
            CreateCompletionSource();

        var analysisService =
            new TestVideoFileAnalysisService(
                async (_, _, cancellationToken) =>
                {
                    analysisStarted.TrySetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        analysisCancelled.TrySetResult();
                        throw;
                    }

                    return SuccessfulResult();
                });

        var queue =
            new VideoFileAnalysisQueue(
                analysisService,
                new TestStaticThumbnailGenerationQueue(),
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            CreateRequest(
            @"C:\Archive\Video.mp4"));

        await analysisStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await queue.DisposeAsync();

        await analysisCancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Worker_ErrorDoesNotStopFollowingWork()
    {
        var secondFileCompleted =
            CreateCompletionSource();

        var callCount = 0;

        var analysisService =
            new TestVideoFileAnalysisService(
                (_, _, _) =>
                {
                    var currentCall =
                        Interlocked.Increment(
                            ref callCount);

                    if (currentCall == 1)
                    {
                        throw new InvalidOperationException(
                            "Test analysis failure.");
                    }

                    secondFileCompleted.TrySetResult();

                    return Task.FromResult(
                        SuccessfulResult());
                });

        await using var queue =
            new VideoFileAnalysisQueue(
                analysisService,
                new TestStaticThumbnailGenerationQueue(),
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            CreateRequest(
            @"C:\Archive\Broken.mp4"));

        await queue.EnqueueAsync(
            CreateRequest(
            @"C:\Archive\Working.mp4"));

        await secondFileCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(
            2,
            Volatile.Read(ref callCount));
    }

    [Fact]
    public async Task SuccessfulVideoAnalysis_EnqueuesThumbnail()
    {
        const string videoPath =
            @"C:\Archive\ConfirmedVideo.mp4";

        var thumbnailQueue =
            new TestStaticThumbnailGenerationQueue();

        var analysisService =
            new TestVideoFileAnalysisService(
                (_, _, _) =>
                    Task.FromResult(
                        new VideoFileAnalysisResult(
                            WasStored: true,
                            State:
                                VideoFileAnalysisState
                                    .Succeeded,
                            HasVideoStream: true,
                            DiagnosticMessage:
                                string.Empty)));

        await using var queue =
            new VideoFileAnalysisQueue(
                analysisService,
                thumbnailQueue,
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        var request = CreateRequest(videoPath);

        await queue.EnqueueAsync(request);

        await thumbnailQueue.RequestEnqueued.Task
            .WaitAsync(TimeSpan.FromSeconds(5));

        var thumbnailRequest =
            Assert.Single(thumbnailQueue.Requests);

        Assert.Equal(
            request.FullPath,
            thumbnailRequest.VideoPath);

        Assert.Equal(
            request.SizeBytes,
            thumbnailRequest.SizeBytes);

        Assert.Equal(
            request.LastWriteTimeUtc,
            thumbnailRequest.LastWriteTimeUtc);
    }

    [Theory]
    [InlineData(
        true,
        false,
        VideoFileAnalysisState.Succeeded)]
    [InlineData(
        false,
        true,
        VideoFileAnalysisState.Succeeded)]
    [InlineData(
        true,
        true,
        VideoFileAnalysisState.Failed)]
    public async Task AnalysisWithoutConfirmedStoredVideo_DoesNotEnqueueThumbnail(

        bool wasStored,
        bool hasVideoStream,
        VideoFileAnalysisState state)
    {
        const string candidatePath =
            @"C:\Archive\Candidate.mp4";

        const string barrierPath =
            @"C:\Archive\Barrier.mp4";

        var barrierStarted =
            CreateCompletionSource();

        var thumbnailQueue =
            new TestStaticThumbnailGenerationQueue();

        var analysisService =
            new TestVideoFileAnalysisService(
                (_, fullPath, _) =>
                {
                    if (fullPath == barrierPath)
                    {
                        barrierStarted.TrySetResult();

                        return Task.FromResult(
                            new VideoFileAnalysisResult(
                                WasStored: true,
                                State:
                                    VideoFileAnalysisState
                                        .Succeeded,
                                HasVideoStream: false,
                                DiagnosticMessage:
                                    string.Empty));
                    }

                    return Task.FromResult(
                        new VideoFileAnalysisResult(
                            WasStored: wasStored,
                            State: state,
                            HasVideoStream:
                                hasVideoStream,
                            DiagnosticMessage:
                                string.Empty));
                });

        await using var queue =
            new VideoFileAnalysisQueue(
                analysisService,
                thumbnailQueue,
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            CreateRequest(candidatePath));

        await queue.EnqueueAsync(
            CreateRequest(barrierPath));

        await barrierStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Empty(thumbnailQueue.Requests);
    }


    private static VideoFileAnalysisRequest
        CreateRequest(string fullPath)
    {
        return new VideoFileAnalysisRequest(
            RootSourceId: RootSourceId,
            FullPath: fullPath,
            SizeBytes: 1024,
            LastWriteTimeUtc:
                new DateTimeOffset(
                    2026,
                    8,
                    14,
                    10,
                    0,
                    0,
                    TimeSpan.Zero));
    }

    private static TaskCompletionSource
        CreateCompletionSource()
    {
        return new TaskCompletionSource(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
    }

    private static VideoFileAnalysisResult
        SuccessfulResult()
    {
        return new VideoFileAnalysisResult(
            WasStored: true,
            State:
                VideoFileAnalysisState.Succeeded,
            HasVideoStream: true,
            DiagnosticMessage: string.Empty);
    }

    private static void UpdateMaximum(
        ref int maximum,
        int candidate)
    {
        while (true)
        {
            var current =
                Volatile.Read(ref maximum);

            if (candidate <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref maximum,
                    candidate,
                    current) == current)
            {
                return;
            }
        }
    }

    private sealed class TestVideoFileAnalysisService
        : IVideoFileAnalysisService
    {
        private readonly Func<
            Guid,
            string,
            CancellationToken,
            Task<VideoFileAnalysisResult>> _analyze;

        public TestVideoFileAnalysisService(
            Func<
                Guid,
                string,
                CancellationToken,
                Task<VideoFileAnalysisResult>> analyze)
        {
            _analyze = analyze;
        }

        public Task<VideoFileAnalysisResult>
            AnalyzeAsync(
                Guid rootSourceId,
                string fullPath,
                CancellationToken cancellationToken = default)
        {
            return _analyze(
                rootSourceId,
                fullPath,
                cancellationToken);
        }
    }

    private sealed class
        TestStaticThumbnailGenerationQueue
        : IStaticThumbnailGenerationQueue
    {
        public TaskCompletionSource RequestEnqueued
        { get; } =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        public List<StaticThumbnailRequest>
            Requests
        { get; } = [];

        public ValueTask EnqueueAsync(
            StaticThumbnailRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken
                .ThrowIfCancellationRequested();

            Requests.Add(request);
            RequestEnqueued.TrySetResult();

            return ValueTask.CompletedTask;
        }

        public Task WaitForIdleAsync(
            CancellationToken cancellationToken =
                default)
        {
            return Task.CompletedTask;
        }
    }


}
