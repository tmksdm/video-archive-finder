using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class VideoFileAnalysisQueue
    : IVideoFileAnalysisQueue,
      IAsyncDisposable,
      IDisposable
{
    private readonly Channel<VideoFileAnalysisRequest>
        _channel;

    private readonly IVideoFileAnalysisService
        _analysisService;

    private readonly IStaticThumbnailGenerationQueue
        _thumbnailGenerationQueue;

    private readonly ILogger<VideoFileAnalysisQueue>
        _logger;

    private readonly CancellationTokenSource
        _stoppingTokenSource = new();

    private readonly Task[] _workers;

    private int _isDisposed;

    public VideoFileAnalysisQueue(
        IVideoFileAnalysisService analysisService,
        IStaticThumbnailGenerationQueue
            thumbnailGenerationQueue,
        ILogger<VideoFileAnalysisQueue> logger,
        int maximumParallelism = 2,
        int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(
            analysisService);

        ArgumentNullException.ThrowIfNull(
            thumbnailGenerationQueue);

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

        _analysisService = analysisService;
        _thumbnailGenerationQueue =
            thumbnailGenerationQueue;
        _logger = logger;

        _channel =
            Channel.CreateBounded<
                VideoFileAnalysisRequest>(
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
                            _stoppingTokenSource
                                .Token)))
                .ToArray();
    }

    public async ValueTask EnqueueAsync(
        VideoFileAnalysisRequest request,
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
            request.FullPath))
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
            when (Volatile.Read(
                ref _isDisposed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(VideoFileAnalysisQueue));
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
                        await _analysisService
                            .AnalyzeAsync(
                                request.RootSourceId,
                                request.FullPath,
                                stoppingToken);

                    if (result.WasStored &&
                        result.State ==
                            VideoFileAnalysisState
                                .Succeeded &&
                        result.HasVideoStream is true)
                    {
                        await _thumbnailGenerationQueue
                            .EnqueueAsync(
                                new StaticThumbnailRequest(
                                    RootSourceId:
                                    request.RootSourceId,
                                    VideoPath:
                                        request.FullPath,
                                    SizeBytes:
                                        request.SizeBytes,
                                    LastWriteTimeUtc:
                                        request
                                            .LastWriteTimeUtc),
                                stoppingToken);
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
                        "Background video processing failed " +
                        "for {VideoPath}. Queue processing " +
                        "will continue.",
                        request.FullPath);
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
