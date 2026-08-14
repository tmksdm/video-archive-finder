using Microsoft.Extensions.Logging.Abstractions;
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
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 2,
                capacity: 8);

        for (var index = 0; index < 4; index++)
        {
            await queue.EnqueueAsync(
                RootSourceId,
                $@"C:\Archive\Video{index}.mp4");
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
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            RootSourceId,
            @"C:\Archive\Video.mp4");

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
                NullLogger<VideoFileAnalysisQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            RootSourceId,
            @"C:\Archive\Broken.mp4");

        await queue.EnqueueAsync(
            RootSourceId,
            @"C:\Archive\Working.mp4");

        await secondFileCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(
            2,
            Volatile.Read(ref callCount));
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
}
