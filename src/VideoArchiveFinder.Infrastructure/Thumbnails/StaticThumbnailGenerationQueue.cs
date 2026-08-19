using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class StaticThumbnailGenerationQueue
    : IStaticThumbnailGenerationQueue,
      IStaticThumbnailStateChangeSource,
      IAsyncDisposable,
      IDisposable
{
    private readonly Channel<StaticThumbnailRequest>
        _channel;

    private readonly IStaticThumbnailGenerator
        _thumbnailGenerator;

    private readonly IVideoFileIndexRepository
        _videoFileIndexRepository;

    private readonly ILogger<StaticThumbnailGenerationQueue>
        _logger;

    private readonly CancellationTokenSource
        _stoppingTokenSource = new();

    private readonly Task[] _workers;

    private int _isDisposed;

    public event EventHandler<
        StaticThumbnailStateChangedEventArgs>?
        StateChanged;

    public StaticThumbnailGenerationQueue(
        IStaticThumbnailGenerator thumbnailGenerator,
        IVideoFileIndexRepository videoFileIndexRepository,
        ILogger<StaticThumbnailGenerationQueue> logger,
        int maximumParallelism = 2,
        int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(
            thumbnailGenerator);

        ArgumentNullException.ThrowIfNull(
            videoFileIndexRepository);

        ArgumentNullException.ThrowIfNull(logger);

        if (maximumParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumParallelism),
                "Maximum parallelism must be positive.");
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Queue capacity must be positive.");
        }

        _thumbnailGenerator = thumbnailGenerator;
        _videoFileIndexRepository =
            videoFileIndexRepository;
        _logger = logger;

        _channel =
            Channel.CreateBounded<StaticThumbnailRequest>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode =
                        BoundedChannelFullMode.Wait,
                    SingleReader =
                        maximumParallelism == 1,
                    SingleWriter = false,
                    AllowSynchronousContinuations =
                        false
                });

        _workers =
            Enumerable.Range(
                    0,
                    maximumParallelism)
                .Select(
                    _ => Task.Run(
                        () => ProcessQueueAsync(
                            _stoppingTokenSource.Token)))
                .ToArray();
    }

    public async ValueTask EnqueueAsync(
        StaticThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(
            request.VideoPath))
        {
            throw new ArgumentException(
                "Video path cannot be empty.",
                nameof(request));
        }

        if (request.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Video file size cannot be negative.");
        }

        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _isDisposed) != 0,
            this);

        try
        {
            await _channel.Writer.WriteAsync(
                request,
                cancellationToken);
        }
        catch (ChannelClosedException)
            when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(StaticThumbnailGenerationQueue));
        }
    }

    public void Dispose()
    {
        DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref _isDisposed,
                1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _stoppingTokenSource.Cancel();

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
            when (_stoppingTokenSource
                .IsCancellationRequested)
        {
            // Остановка очереди является штатной.
        }
        finally
        {
            _stoppingTokenSource.Dispose();
        }
    }

    private async Task ProcessQueueAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in
                _channel.Reader.ReadAllAsync(
                    stoppingToken))
            {
                try
                {
                    await ProcessRequestAsync(
                        request,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken
                        .IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Background static thumbnail " +
                        "processing failed for {VideoPath}. " +
                        "Queue processing will continue.",
                        request.VideoPath);
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken
                .IsCancellationRequested)
        {
            // Остановка очереди является штатной.
        }
    }

    private async Task ProcessRequestAsync(
        StaticThumbnailRequest request,
        CancellationToken stoppingToken)
    {
        var pendingWasStored =
            await UpdateThumbnailAsync(
                request,
                VideoFileThumbnailState.Pending,
                thumbnailPath: null,
                stoppingToken);

        if (!pendingWasStored)
        {
            _logger.LogInformation(
                "Static thumbnail request for {VideoPath} " +
                "is stale and will not be processed.",
                request.VideoPath);

            return;
        }

        StaticThumbnailGenerationResult result;

        try
        {
            result =
                await _thumbnailGenerator.GenerateAsync(
                    request,
                    stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Static thumbnail generation threw an " +
                "exception for {VideoPath}.",
                request.VideoPath);

            await TryStoreFailedStateAsync(
                request,
                stoppingToken);

            return;
        }

        if (result.IsSuccess &&
            !string.IsNullOrWhiteSpace(
                result.ThumbnailPath))
        {
            await StoreFinalStateAsync(
                request,
                VideoFileThumbnailState.Succeeded,
                result.ThumbnailPath,
                stoppingToken);

            return;
        }

        if (result.IsSuccess)
        {
            _logger.LogWarning(
                "Static thumbnail generation reported " +
                "success without a thumbnail path for " +
                "{VideoPath}.",
                request.VideoPath);
        }
        else
        {
            _logger.LogWarning(
                "Static thumbnail generation did not " +
                "succeed for {VideoPath}. Status: " +
                "{Status}. Details: {DiagnosticMessage}",
                request.VideoPath,
                result.Status,
                result.DiagnosticMessage);
        }

        await StoreFinalStateAsync(
            request,
            VideoFileThumbnailState.Failed,
            thumbnailPath: null,
            stoppingToken);
    }

    private async Task StoreFinalStateAsync(
        StaticThumbnailRequest request,
        VideoFileThumbnailState state,
        string? thumbnailPath,
        CancellationToken cancellationToken)
    {
        var wasStored =
            await UpdateThumbnailAsync(
                request,
                state,
                thumbnailPath,
                cancellationToken);

        if (!wasStored)
        {
            _logger.LogInformation(
                "Static thumbnail result for {VideoPath} " +
                "was not stored because the indexed file " +
                "changed or was removed.",
                request.VideoPath);
        }
    }

    private async Task TryStoreFailedStateAsync(
        StaticThumbnailRequest request,
        CancellationToken stoppingToken)
    {
        try
        {
            await StoreFinalStateAsync(
                request,
                VideoFileThumbnailState.Failed,
                thumbnailPath: null,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to persist the thumbnail failure " +
                "state for {VideoPath}.",
                request.VideoPath);
        }
    }

    private async Task<bool> UpdateThumbnailAsync(
        StaticThumbnailRequest request,
        VideoFileThumbnailState state,
        string? thumbnailPath,
        CancellationToken cancellationToken)
    {
        var wasUpdated =
            await _videoFileIndexRepository
                .UpdateThumbnailAsync(
                    new VideoFileThumbnailUpdate(
                        RootSourceId:
                            request.RootSourceId,
                        FullPath:
                            request.VideoPath,
                        SizeBytes:
                            request.SizeBytes,
                        LastWriteTimeUtc:
                            request.LastWriteTimeUtc,
                        State:
                            state,
                        ThumbnailPath:
                            thumbnailPath),
                    cancellationToken);

        if (wasUpdated)
        {
            PublishStateChanged(
                request,
                state,
                thumbnailPath);
        }

        return wasUpdated;
    }

    private void PublishStateChanged(
        StaticThumbnailRequest request,
        VideoFileThumbnailState state,
        string? thumbnailPath)
    {
        var handlers = StateChanged;

        if (handlers is null)
        {
            return;
        }

        var eventArgs =
            new StaticThumbnailStateChangedEventArgs(
                request,
                state,
                thumbnailPath);

        foreach (EventHandler<
                     StaticThumbnailStateChangedEventArgs>
                 handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "A thumbnail state notification " +
                    "handler failed for {VideoPath}.",
                    request.VideoPath);
            }
        }
    }


}
