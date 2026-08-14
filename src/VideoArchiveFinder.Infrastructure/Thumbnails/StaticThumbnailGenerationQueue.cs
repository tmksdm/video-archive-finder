using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class StaticThumbnailGenerationQueue
    : IStaticThumbnailGenerationQueue,
      IAsyncDisposable,
      IDisposable
{
    private readonly Channel<StaticThumbnailRequest>
        _channel;

    private readonly IStaticThumbnailGenerator
        _thumbnailGenerator;

    private readonly ILogger<StaticThumbnailGenerationQueue>
        _logger;

    private readonly CancellationTokenSource
        _stoppingTokenSource = new();

    private readonly Task[] _workers;

    private int _isDisposed;

    public StaticThumbnailGenerationQueue(
        IStaticThumbnailGenerator thumbnailGenerator,
        ILogger<StaticThumbnailGenerationQueue> logger,
        int maximumParallelism = 2,
        int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(
            thumbnailGenerator);

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
                    var result =
                        await _thumbnailGenerator
                            .GenerateAsync(
                                request,
                                stoppingToken);

                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning(
                            "Static thumbnail generation " +
                            "did not succeed for {VideoPath}. " +
                            "Status: {Status}. Details: " +
                            "{DiagnosticMessage}",
                            request.VideoPath,
                            result.Status,
                            result.DiagnosticMessage);
                    }
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
                        "generation failed for {VideoPath}. " +
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
}
