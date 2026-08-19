using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class StaticThumbnailGenerationQueueTests
{
    [Fact]
    public async Task Queue_DoesNotExceedConfiguredParallelism()
    {
        var twoWorkersStarted =
            CreateCompletionSource();

        var allRequestsCompleted =
            CreateCompletionSource();

        var releaseWorkers =
            CreateCompletionSource();

        var runningCount = 0;
        var maximumRunningCount = 0;
        var startedCount = 0;
        var completedCount = 0;

        var generator =
            new TestStaticThumbnailGenerator(
                async (_, cancellationToken) =>
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
                        allRequestsCompleted.TrySetResult();
                    }

                    return SuccessfulResult();
                });

        await using var queue =
            new StaticThumbnailGenerationQueue(
                generator,
                new RecordingVideoFileIndexRepository(),
                NullLogger<
                    StaticThumbnailGenerationQueue>.Instance,
                maximumParallelism: 2,
                capacity: 8);

        for (var index = 0; index < 4; index++)
        {
            await queue.EnqueueAsync(
                CreateRequest(index));
        }

        await twoWorkersStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await Task.Delay(100);

        Assert.Equal(
            2,
            Volatile.Read(
                ref maximumRunningCount));

        releaseWorkers.TrySetResult();

        await allRequestsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EnqueueAsync_FullQueue_WaitsForSpace()
    {
        var firstRequestStarted =
            CreateCompletionSource();

        var releaseGenerator =
            CreateCompletionSource();

        var generator =
            new TestStaticThumbnailGenerator(
                async (_, cancellationToken) =>
                {
                    firstRequestStarted.TrySetResult();

                    await releaseGenerator.Task
                        .WaitAsync(cancellationToken);

                    return SuccessfulResult();
                });

        await using var queue =
            new StaticThumbnailGenerationQueue(
                generator,
                new RecordingVideoFileIndexRepository(),
                NullLogger<
                    StaticThumbnailGenerationQueue>.Instance,
                maximumParallelism: 1,
                capacity: 1);

        await queue.EnqueueAsync(
            CreateRequest(1));

        await firstRequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await queue.EnqueueAsync(
            CreateRequest(2));

        using var cancellationSource =
            new CancellationTokenSource();

        var thirdEnqueue =
            queue.EnqueueAsync(
                    CreateRequest(3),
                    cancellationSource.Token)
                .AsTask();

        await Task.Delay(100);

        Assert.False(thirdEnqueue.IsCompleted);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
                () => thirdEnqueue);

        releaseGenerator.TrySetResult();
    }

    [Fact]
    public async Task DisposeAsync_CancelsRunningGeneration()
    {
        var generationStarted =
            CreateCompletionSource();

        var generationCancelled =
            CreateCompletionSource();

        var generator =
            new TestStaticThumbnailGenerator(
                async (_, cancellationToken) =>
                {
                    generationStarted.TrySetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        generationCancelled.TrySetResult();
                        throw;
                    }

                    return SuccessfulResult();
                });

        var queue =
            new StaticThumbnailGenerationQueue(
                generator,
                new RecordingVideoFileIndexRepository(),
                NullLogger<
                    StaticThumbnailGenerationQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            CreateRequest(1));

        await generationStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await queue.DisposeAsync();

        await generationCancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Worker_ErrorDoesNotStopFollowingWork()
    {
        var secondRequestCompleted =
            CreateCompletionSource();

        var callCount = 0;

        var generator =
            new TestStaticThumbnailGenerator(
                (_, _) =>
                {
                    var currentCall =
                        Interlocked.Increment(
                            ref callCount);

                    if (currentCall == 1)
                    {
                        throw new InvalidOperationException(
                            "Test thumbnail failure.");
                    }

                    secondRequestCompleted.TrySetResult();

                    return Task.FromResult(
                        SuccessfulResult());
                });

        await using var queue =
            new StaticThumbnailGenerationQueue(
                generator,
                new RecordingVideoFileIndexRepository(),
                NullLogger<
                    StaticThumbnailGenerationQueue>.Instance,
                maximumParallelism: 1,
                capacity: 4);

        await queue.EnqueueAsync(
            CreateRequest(1));

        await queue.EnqueueAsync(
            CreateRequest(2));

        await secondRequestCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(
            2,
            Volatile.Read(ref callCount));
    }

    private static StaticThumbnailRequest CreateRequest(
        int index)
    {
        return new StaticThumbnailRequest(
            RootSourceId:
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"),
            VideoPath:
                $@"C:\Archive\Video{index}.mp4",
            SizeBytes: 1_024 + index,
            LastWriteTimeUtc:
                new DateTimeOffset(
                    2026,
                    8,
                    14,
                    10,
                    0,
                    index,
                    TimeSpan.Zero));
    }

    private static StaticThumbnailGenerationResult
        SuccessfulResult()
    {
        return new StaticThumbnailGenerationResult(
            Status:
                StaticThumbnailGenerationStatus.Generated,
            ThumbnailPath:
                @"C:\Cache\thumbnail.jpg",
            ExitCode: 0,
            DiagnosticMessage: string.Empty);
    }

    private static TaskCompletionSource
        CreateCompletionSource()
    {
        return new TaskCompletionSource(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
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

    private sealed class TestStaticThumbnailGenerator
        : IStaticThumbnailGenerator
    {
        private readonly Func<
            StaticThumbnailRequest,
            CancellationToken,
            Task<StaticThumbnailGenerationResult>>
            _generate;

        public TestStaticThumbnailGenerator(
            Func<
                StaticThumbnailRequest,
                CancellationToken,
                Task<StaticThumbnailGenerationResult>>
                generate)
        {
            _generate = generate;
        }

        public Task<StaticThumbnailGenerationResult>
            GenerateAsync(
                StaticThumbnailRequest request,
                CancellationToken cancellationToken = default)
        {
            return _generate(
                request,
                cancellationToken);
        }
    }
}
