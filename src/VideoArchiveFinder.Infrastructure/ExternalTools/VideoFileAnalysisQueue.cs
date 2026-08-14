using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class VideoFileAnalysisQueue
    : IVideoFileAnalysisQueue,
      IAsyncDisposable,
      IDisposable
{
    private readonly Channel<AnalysisWorkItem>
        _channel;

    private readonly IVideoFileAnalysisService
        _analysisService;

    private readonly ILogger<VideoFileAnalysisQueue>
        _logger;

    private readonly CancellationTokenSource
        _stoppingTokenSource = new();

    private readonly Task[] _workers;

    private int _isDisposed;

    public VideoFileAnalysisQueue(
        IVideoFileAnalysisService analysisService,
        ILogger<VideoFileAnalysisQueue> logger,
        int maximumParallelism = 2,
        int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(
            analysisService);

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
        _logger = logger;

        _channel =
            Channel.CreateBounded<AnalysisWorkItem>(
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
        Guid rootSourceId,
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            fullPath);

        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _isDisposed) != 0,
            this);

        var workItem =
            new AnalysisWorkItem(
                rootSourceId,
                fullPath);

        try
        {
            await _channel.Writer.WriteAsync(
                workItem,
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
            await foreach (var workItem in
                _channel.Reader.ReadAllAsync(
                    stoppingToken))
            {
                try
                {
                    await _analysisService
                        .AnalyzeAsync(
                            workItem.RootSourceId,
                            workItem.FullPath,
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
                        "Background video analysis failed " +
                        "for {VideoPath}. Queue processing " +
                        "will continue.",
                        workItem.FullPath);
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

    private sealed record AnalysisWorkItem(
        Guid RootSourceId,
        string FullPath);
}
