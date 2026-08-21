using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.ExternalTools;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class StaticThumbnailGeneratorTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            nameof(StaticThumbnailGeneratorTests),
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_FfmpegIsMissing_ReturnsToolUnavailable()
    {
        var processRunner = CreateProcessRunner();

        var generator = CreateGenerator(
            processRunner,
            ffmpegExists: false);

        var result = await generator.GenerateAsync(
            CreateRequest(CreateVideoFile()));

        Assert.Equal(
            StaticThumbnailGenerationStatus.ToolUnavailable,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ThumbnailPath);
        Assert.Equal(0, processRunner.CallCount);
        Assert.Contains(
            "ffmpeg.exe",
            result.DiagnosticMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_VideoIsMissing_ReturnsInputUnavailable()
    {
        var processRunner = CreateProcessRunner();

        var generator = CreateGenerator(
            processRunner);

        var missingPath = Path.Combine(
            _temporaryDirectory,
            "missing video.mp4");

        var result = await generator.GenerateAsync(
            CreateRequest(missingPath));

        Assert.Equal(
            StaticThumbnailGenerationStatus.InputUnavailable,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ThumbnailPath);
        Assert.Equal(0, processRunner.CallCount);
        Assert.Contains(
            missingPath,
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task GenerateAsync_Success_CreatesCachedThumbnail()
    {
        var processRunner =
            new FakeExternalProcessRunner(
                request =>
                {
                    File.WriteAllText(
                        request.Arguments[^1],
                        "jpeg-content");

                    return CreateCompletedResult();
                });

        var toolsStatus = CreateToolsStatus();

        var generator = CreateGenerator(
            processRunner,
            toolsStatus: toolsStatus);

        var videoPath = CreateVideoFile(
            "video with spaces.mp4");

        var result = await generator.GenerateAsync(
            CreateRequest(videoPath));

        Assert.Equal(
            StaticThumbnailGenerationStatus.Generated,
            result.Status);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.ThumbnailPath);
        Assert.True(File.Exists(result.ThumbnailPath));
        Assert.True(
            new FileInfo(result.ThumbnailPath).Length > 0);

        var expectedCacheRoot = Path.Combine(
            _temporaryDirectory,
            "app-data",
            "Cache",
            "Thumbnails",
            "v1");

        Assert.StartsWith(
            expectedCacheRoot,
            result.ThumbnailPath,
            StringComparison.OrdinalIgnoreCase);


        var processRequest =
            Assert.IsType<ExternalProcessRequest>(
                processRunner.LastRequest);

        Assert.Equal(
            toolsStatus.FfmpegPath,
            processRequest.FileName);

        Assert.Equal(
            TimeSpan.FromMinutes(1),
            processRequest.Timeout);

        Assert.Contains(
            videoPath,
            processRequest.Arguments);

        Assert.Contains(
            "scale=480:-2:" +
            "force_original_aspect_ratio=decrease",
            processRequest.Arguments);

        Assert.Equal(
            processRequest.Arguments[^1],
            Path.ChangeExtension(
                processRequest.Arguments[^1],
                ".jpg"));

        Assert.NotEqual(
            processRequest.Arguments[^1],
            result.ThumbnailPath);

        Assert.False(
            File.Exists(processRequest.Arguments[^1]));
    }

    [Fact]
    public async Task GenerateAsync_ThumbnailExists_ReturnsCacheHit()
    {
        var processRunner =
            new FakeExternalProcessRunner(
                request =>
                {
                    File.WriteAllText(
                        request.Arguments[^1],
                        "jpeg-content");

                    return CreateCompletedResult();
                });

        var generator = CreateGenerator(
            processRunner);

        var request = CreateRequest(
            CreateVideoFile());

        var firstResult =
            await generator.GenerateAsync(request);

        var secondResult =
            await generator.GenerateAsync(request);

        Assert.Equal(
            StaticThumbnailGenerationStatus.Generated,
            firstResult.Status);

        Assert.Equal(
            StaticThumbnailGenerationStatus.CacheHit,
            secondResult.Status);

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(
            firstResult.ThumbnailPath,
            secondResult.ThumbnailPath);

        Assert.Equal(1, processRunner.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ProcessTimesOut_DeletesTemporaryFile()
    {
        string? temporaryPath = null;

        var processRunner =
            new FakeExternalProcessRunner(
                request =>
                {
                    temporaryPath =
                        request.Arguments[^1];

                    File.WriteAllText(
                        temporaryPath,
                        "partial-content");

                    return new ExternalProcessResult(
                        ExternalProcessRunStatus.TimedOut,
                        null,
                        string.Empty,
                        string.Empty,
                        "Время ожидания FFmpeg истекло.");
                });

        var generator = CreateGenerator(
            processRunner);

        var result = await generator.GenerateAsync(
            CreateRequest(CreateVideoFile()));

        Assert.Equal(
            StaticThumbnailGenerationStatus.TimedOut,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ThumbnailPath);
        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task GenerateAsync_NonZeroExitCode_ReturnsFailure()
    {
        string? temporaryPath = null;

        var processRunner =
            new FakeExternalProcessRunner(
                request =>
                {
                    temporaryPath =
                        request.Arguments[^1];

                    File.WriteAllText(
                        temporaryPath,
                        "partial-content");

                    return CreateCompletedResult(
                        exitCode: 1,
                        standardError:
                            "Invalid video stream.");
                });

        var generator = CreateGenerator(
            processRunner);

        var result = await generator.GenerateAsync(
            CreateRequest(CreateVideoFile()));

        Assert.Equal(
            StaticThumbnailGenerationStatus.Failed,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.ThumbnailPath);
        Assert.Contains(
            "Invalid video stream.",
            result.DiagnosticMessage);

        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task GenerateAsync_NoOutputFile_ReturnsFailure()
    {
        var processRunner = CreateProcessRunner();

        var generator = CreateGenerator(
            processRunner);

        var result = await generator.GenerateAsync(
            CreateRequest(CreateVideoFile()));

        Assert.Equal(
            StaticThumbnailGenerationStatus.Failed,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ThumbnailPath);
        Assert.Contains(
            "без создания",
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task GenerateAsync_ProcessIsCancelled_DeletesTemporaryFile()
    {
        string? temporaryPath = null;

        using var cancellationSource =
            new CancellationTokenSource();

        var processRunner =
            new FakeExternalProcessRunner(
                request =>
                {
                    temporaryPath =
                        request.Arguments[^1];

                    File.WriteAllText(
                        temporaryPath,
                        "partial-content");

                    throw new OperationCanceledException(
                        cancellationSource.Token);
                });

        var generator = CreateGenerator(
            processRunner);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => generator.GenerateAsync(
                CreateRequest(CreateVideoFile()),
                cancellationSource.Token));

        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private StaticThumbnailGenerator CreateGenerator(
        FakeExternalProcessRunner processRunner,
        bool ffmpegExists = true,
        FfmpegToolsStatus? toolsStatus = null)
    {
        var cachePathProvider =
            new ThumbnailCachePathProvider(
                new FakeApplicationDataDirectoryProvider(
                    Path.Combine(
                        _temporaryDirectory,
                        "app-data")),
                new ThumbnailCacheKeyGenerator());

        return new StaticThumbnailGenerator(
            new FakeFfmpegToolsLocator(
                toolsStatus ??
                CreateToolsStatus(ffmpegExists)),
            processRunner,
            cachePathProvider,
            NullLogger<
                StaticThumbnailGenerator>.Instance);
    }

    private string CreateVideoFile(
        string fileName = "video.mp4")
    {
        Directory.CreateDirectory(
            _temporaryDirectory);

        var videoPath = Path.Combine(
            _temporaryDirectory,
            fileName);

        File.WriteAllText(
            videoPath,
            "video-content");

        return videoPath;
    }

    private static StaticThumbnailRequest CreateRequest(
        string videoPath)
    {
        return new StaticThumbnailRequest(
            RootSourceId:
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"),
            videoPath,
            13,
            new DateTimeOffset(
                2026,
                8,
                14,
                10,
                0,
                0,
                TimeSpan.Zero));
    }

    private static FfmpegToolsStatus CreateToolsStatus(
        bool ffmpegExists = true)
    {
        var toolsDirectory = Path.Combine(
            Path.GetTempPath(),
            "app",
            "tools");

        return new FfmpegToolsStatus(
            toolsDirectory,
            Path.Combine(
                toolsDirectory,
                "ffmpeg.exe"),
            Path.Combine(
                toolsDirectory,
                "ffprobe.exe"),
            ffmpegExists,
            true);
    }

    private static FakeExternalProcessRunner
        CreateProcessRunner()
    {
        return new FakeExternalProcessRunner(
            _ => CreateCompletedResult());
    }

    private static ExternalProcessResult
        CreateCompletedResult(
            int exitCode = 0,
            string standardError = "")
    {
        return new ExternalProcessResult(
            ExternalProcessRunStatus.Completed,
            exitCode,
            string.Empty,
            standardError,
            "Внешний процесс завершён.");
    }

    private sealed class FakeFfmpegToolsLocator(
        FfmpegToolsStatus status)
        : IFfmpegToolsLocator
    {
        public FfmpegToolsStatus Locate()
        {
            return status;
        }
    }

    private sealed class
        FakeApplicationDataDirectoryProvider(
            string applicationDataDirectory)
        : IApplicationDataDirectoryProvider
    {
        public string GetApplicationDataDirectory()
        {
            return applicationDataDirectory;
        }
    }

    private sealed class FakeExternalProcessRunner(
        Func<
            ExternalProcessRequest,
            ExternalProcessResult> handler)
        : IExternalProcessRunner
    {
        public int CallCount { get; private set; }

        public ExternalProcessRequest? LastRequest
        {
            get;
            private set;
        }

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastRequest = request;

            return Task.FromResult(handler(request));
        }
    }
}
