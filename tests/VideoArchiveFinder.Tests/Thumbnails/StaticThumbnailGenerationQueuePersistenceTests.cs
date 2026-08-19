using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class
    StaticThumbnailGenerationQueuePersistenceTests
{
    [Fact]
    public async Task SuccessfulGeneration_PersistsPendingThenSucceeded()
    {
        var request = CreateRequest();

        var completed = CreateCompletionSource();

        var repository =
            new RecordingVideoFileIndexRepository
            {
                Updated = update =>
                {
                    if (update.State ==
                        VideoFileThumbnailState.Succeeded)
                    {
                        completed.TrySetResult();
                    }
                }
            };

        var generator =
            new TestStaticThumbnailGenerator(
                (_, _) => Task.FromResult(
                    new StaticThumbnailGenerationResult(
                        Status:
                            StaticThumbnailGenerationStatus
                                .Generated,
                        ThumbnailPath:
                            @"C:\Cache\thumbnail.jpg",
                        ExitCode: 0,
                        DiagnosticMessage:
                            string.Empty)));

        await using var queue =
            CreateQueue(
                generator,
                repository);

        await queue.EnqueueAsync(request);

        await completed.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var updates = repository.Updates.ToArray();

        Assert.Collection(
            updates,
            pending =>
            {
                Assert.Equal(
                    VideoFileThumbnailState.Pending,
                    pending.State);

                Assert.Null(pending.ThumbnailPath);
                AssertMatchesRequest(request, pending);
            },
            succeeded =>
            {
                Assert.Equal(
                    VideoFileThumbnailState.Succeeded,
                    succeeded.State);

                Assert.Equal(
                    @"C:\Cache\thumbnail.jpg",
                    succeeded.ThumbnailPath);

                AssertMatchesRequest(request, succeeded);
            });
    }

    [Fact]
    public async Task UnsuccessfulGeneration_PersistsPendingThenFailed()
    {
        var request = CreateRequest();

        var completed = CreateCompletionSource();

        var repository =
            new RecordingVideoFileIndexRepository
            {
                Updated = update =>
                {
                    if (update.State ==
                        VideoFileThumbnailState.Failed)
                    {
                        completed.TrySetResult();
                    }
                }
            };

        var generator =
            new TestStaticThumbnailGenerator(
                (_, _) => Task.FromResult(
                    new StaticThumbnailGenerationResult(
                        Status:
                            StaticThumbnailGenerationStatus
                                .ToolUnavailable,
                        ThumbnailPath:
                            @"C:\Cache\must-not-be-saved.jpg",
                        ExitCode: null,
                        DiagnosticMessage:
                            "FFmpeg is unavailable.")));

        await using var queue =
            CreateQueue(
                generator,
                repository);

        await queue.EnqueueAsync(request);

        await completed.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var updates = repository.Updates.ToArray();

        Assert.Collection(
            updates,
            pending =>
            {
                Assert.Equal(
                    VideoFileThumbnailState.Pending,
                    pending.State);

                Assert.Null(pending.ThumbnailPath);
            },
            failed =>
            {
                Assert.Equal(
                    VideoFileThumbnailState.Failed,
                    failed.State);

                Assert.Null(failed.ThumbnailPath);
            });
    }

    [Fact]
    public async Task StalePendingUpdate_DoesNotRunGenerator()
    {
        var pendingAttempted =
            CreateCompletionSource();

        var generatorCallCount = 0;

        var repository =
            new RecordingVideoFileIndexRepository
            {
                UpdateResult = update =>
                {
                    if (update.State ==
                        VideoFileThumbnailState.Pending)
                    {
                        pendingAttempted.TrySetResult();
                    }

                    return false;
                }
            };

        var generator =
            new TestStaticThumbnailGenerator(
                (_, _) =>
                {
                    Interlocked.Increment(
                        ref generatorCallCount);

                    return Task.FromResult(
                        SuccessfulResult());
                });

        await using var queue =
            CreateQueue(
                generator,
                repository);

        await queue.EnqueueAsync(CreateRequest());

        await pendingAttempted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await Task.Delay(100);

        Assert.Equal(
            0,
            Volatile.Read(ref generatorCallCount));

        var update =
            Assert.Single(repository.Updates);

        Assert.Equal(
            VideoFileThumbnailState.Pending,
            update.State);

        Assert.Null(update.ThumbnailPath);
    }

    private static StaticThumbnailGenerationQueue
        CreateQueue(
            IStaticThumbnailGenerator generator,
            IVideoFileIndexRepository repository)
    {
        return new StaticThumbnailGenerationQueue(
            generator,
            repository,
            NullLogger<
                StaticThumbnailGenerationQueue>.Instance,
            maximumParallelism: 1,
            capacity: 4);
    }

    private static StaticThumbnailRequest CreateRequest()
    {
        return new StaticThumbnailRequest(
            RootSourceId:
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"),
            VideoPath:
                @"C:\Archive\Video.mp4",
            SizeBytes: 1_024,
            LastWriteTimeUtc:
                new DateTimeOffset(
                    2026,
                    8,
                    18,
                    10,
                    0,
                    0,
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

    private static void AssertMatchesRequest(
        StaticThumbnailRequest request,
        VideoFileThumbnailUpdate update)
    {
        Assert.Equal(
            request.RootSourceId,
            update.RootSourceId);

        Assert.Equal(
            request.VideoPath,
            update.FullPath);

        Assert.Equal(
            request.SizeBytes,
            update.SizeBytes);

        Assert.Equal(
            request.LastWriteTimeUtc,
            update.LastWriteTimeUtc);
    }

    private static TaskCompletionSource
        CreateCompletionSource()
    {
        return new TaskCompletionSource(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
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

internal sealed class RecordingVideoFileIndexRepository
    : IVideoFileIndexRepository
{
    public ConcurrentQueue<VideoFileThumbnailUpdate>
        Updates
    { get; } = new();

    public Func<VideoFileThumbnailUpdate, bool>
        UpdateResult
    { get; init; } = _ => true;

    public Action<VideoFileThumbnailUpdate>?
        Updated
    { get; init; }

    public Task<bool> UpdateThumbnailAsync(
        VideoFileThumbnailUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Updates.Enqueue(update);
        Updated?.Invoke(update);

        return Task.FromResult(
            UpdateResult(update));
    }

    public Task UpsertBatchAsync(
        IReadOnlyCollection<VideoFileIndexUpsertItem> files,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<bool> UpdateAnalysisAsync(
        VideoFileAnalysisUpdate update,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<int> CompleteFolderScanAsync(
        Guid rootSourceId,
        string folderFullPath,
        DateTimeOffset scanStartedAtUtc,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<IndexedVideoFile>>
        GetByFolderPathAsync(
            Guid rootSourceId,
            string folderFullPath,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
