using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class VideoFileAnalysisService
    : IVideoFileAnalysisService
{
    private readonly IFfprobeRunner _ffprobeRunner;
    private readonly IFfprobeJsonParser _ffprobeJsonParser;
    private readonly IVideoFileIndexRepository
        _videoFileIndexRepository;
    private readonly ILogger<VideoFileAnalysisService>
        _logger;

    public VideoFileAnalysisService(
        IFfprobeRunner ffprobeRunner,
        IFfprobeJsonParser ffprobeJsonParser,
        IVideoFileIndexRepository videoFileIndexRepository,
        ILogger<VideoFileAnalysisService> logger)
    {
        _ffprobeRunner = ffprobeRunner;
        _ffprobeJsonParser = ffprobeJsonParser;
        _videoFileIndexRepository =
            videoFileIndexRepository;
        _logger = logger;
    }

    public async Task<VideoFileAnalysisResult> AnalyzeAsync(
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

        cancellationToken.ThrowIfCancellationRequested();

        var runResult =
            await _ffprobeRunner.RunAsync(
                fullPath,
                cancellationToken);

        if (!runResult.IsSuccess)
        {
            _logger.LogWarning(
                "FFprobe analysis failed for {VideoPath}: " +
                "{DiagnosticMessage}",
                fullPath,
                runResult.DiagnosticMessage);

            return await StoreFailureAsync(
                rootSourceId,
                fullPath,
                runResult.DiagnosticMessage,
                cancellationToken);
        }

        var parseResult =
            _ffprobeJsonParser.Parse(
                runResult.JsonOutput);

        if (!parseResult.IsSuccess)
        {
            _logger.LogWarning(
                "FFprobe output parsing failed for " +
                "{VideoPath}: {DiagnosticMessage}",
                fullPath,
                parseResult.DiagnosticMessage);

            return await StoreFailureAsync(
                rootSourceId,
                fullPath,
                parseResult.DiagnosticMessage,
                cancellationToken);
        }

        var metadata = parseResult.Metadata!;

        var update = new VideoFileAnalysisUpdate(
            RootSourceId: rootSourceId,
            FullPath: fullPath,
            State: VideoFileAnalysisState.Succeeded,
            HasVideoStream: metadata.HasVideoStream,
            Duration: metadata.Duration,
            Width: metadata.Width,
            Height: metadata.Height,
            Codec: metadata.CodecName);

        var wasStored =
            await _videoFileIndexRepository
                .UpdateAnalysisAsync(
                    update,
                    cancellationToken);

        if (!wasStored)
        {
            _logger.LogWarning(
                "Video analysis was not stored because " +
                "the indexed file was not found: {VideoPath}.",
                fullPath);
        }

        return new VideoFileAnalysisResult(
            WasStored: wasStored,
            State: VideoFileAnalysisState.Succeeded,
            DiagnosticMessage:
                parseResult.DiagnosticMessage);
    }

    private async Task<VideoFileAnalysisResult>
        StoreFailureAsync(
            Guid rootSourceId,
            string fullPath,
            string diagnosticMessage,
            CancellationToken cancellationToken)
    {
        var update = new VideoFileAnalysisUpdate(
            RootSourceId: rootSourceId,
            FullPath: fullPath,
            State: VideoFileAnalysisState.Failed,
            HasVideoStream: null,
            Duration: null,
            Width: null,
            Height: null,
            Codec: null);

        var wasStored =
            await _videoFileIndexRepository
                .UpdateAnalysisAsync(
                    update,
                    cancellationToken);

        if (!wasStored)
        {
            _logger.LogWarning(
                "Failed video analysis was not stored " +
                "because the indexed file was not found: " +
                "{VideoPath}.",
                fullPath);
        }

        return new VideoFileAnalysisResult(
            WasStored: wasStored,
            State: VideoFileAnalysisState.Failed,
            DiagnosticMessage: diagnosticMessage);
    }
}
