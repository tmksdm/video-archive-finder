using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.ExternalTools;

public sealed class FfprobeRunnerTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            nameof(FfprobeRunnerTests),
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_FfprobeIsMissing_ReturnsToolUnavailable()
    {
        var toolsStatus = CreateToolsStatus(
            ffprobeExists: false);

        var processRunner = new FakeExternalProcessRunner(
            CreateCompletedProcessResult());

        var runner = new FfprobeRunner(
            new FakeFfmpegToolsLocator(toolsStatus),
            processRunner);

        var result = await runner.RunAsync(
            CreateVideoFile());

        Assert.Equal(
            FfprobeRunStatus.ToolUnavailable,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ExitCode);
        Assert.Empty(result.JsonOutput);
        Assert.Equal(0, processRunner.CallCount);
        Assert.Contains(
            toolsStatus.FfprobePath,
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_VideoIsMissing_ReturnsInputUnavailable()
    {
        var processRunner = new FakeExternalProcessRunner(
            CreateCompletedProcessResult());

        var runner = CreateRunner(processRunner);

        var missingVideoPath = Path.Combine(
            _temporaryDirectory,
            "missing video.mp4");

        var result = await runner.RunAsync(
            missingVideoPath);

        Assert.Equal(
            FfprobeRunStatus.InputUnavailable,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, processRunner.CallCount);
        Assert.Contains(
            missingVideoPath,
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_Success_PassesExpectedArguments()
    {
        const string expectedJson =
            """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "width": 1920,
                  "height": 1080
                }
              ],
              "format": {
                "duration": "12.5"
              }
            }
            """;

        var processRunner = new FakeExternalProcessRunner(
            CreateCompletedProcessResult(
                standardOutput: expectedJson));

        var toolsStatus = CreateToolsStatus(
            ffprobeExists: true);

        var runner = new FfprobeRunner(
            new FakeFfmpegToolsLocator(toolsStatus),
            processRunner);

        var videoPath = CreateVideoFile(
            "video with spaces.mp4");

        var result = await runner.RunAsync(videoPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            FfprobeRunStatus.Succeeded,
            result.Status);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedJson, result.JsonOutput);
        Assert.Equal(1, processRunner.CallCount);

        var request = Assert.IsType<ExternalProcessRequest>(
            processRunner.LastRequest);

        Assert.Equal(
            toolsStatus.FfprobePath,
            request.FileName);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            request.Timeout);

        Assert.Equal(
            new[]
            {
                "-v",
                "error",
                "-print_format",
                "json",
                "-show_entries",
                "format=duration:" +
                "stream=codec_type,codec_name,width,height",
                videoPath
            },
            request.Arguments);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_ReturnsFailure()
    {
        var processRunner = new FakeExternalProcessRunner(
            CreateCompletedProcessResult(
                exitCode: 1,
                standardError: "Invalid data found."));

        var runner = CreateRunner(processRunner);

        var result = await runner.RunAsync(
            CreateVideoFile());

        Assert.Equal(
            FfprobeRunStatus.Failed,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);

        Assert.Contains(
            "Invalid data found.",
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_ProcessTimesOut_ReturnsTimedOut()
    {
        var processResult = new ExternalProcessResult(
            ExternalProcessRunStatus.TimedOut,
            null,
            string.Empty,
            string.Empty,
            "Внешний процесс не завершился за 30 с.");

        var processRunner =
            new FakeExternalProcessRunner(processResult);

        var runner = CreateRunner(processRunner);

        var result = await runner.RunAsync(
            CreateVideoFile());

        Assert.Equal(
            FfprobeRunStatus.TimedOut,
            result.Status);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ExitCode);

        Assert.Contains(
            "30 с",
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_CancellationRequested_ThrowsCancellation()
    {
        var processRunner = new FakeExternalProcessRunner(
            CreateCompletedProcessResult());

        var runner = CreateRunner(processRunner);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                CreateVideoFile(),
                cancellationSource.Token));

        Assert.Equal(0, processRunner.CallCount);
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

    private FfprobeRunner CreateRunner(
        FakeExternalProcessRunner processRunner)
    {
        return new FfprobeRunner(
            new FakeFfmpegToolsLocator(
                CreateToolsStatus(
                    ffprobeExists: true)),
            processRunner);
    }

    private FfmpegToolsStatus CreateToolsStatus(
        bool ffprobeExists)
    {
        var toolsDirectory = Path.Combine(
            _temporaryDirectory,
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
            true,
            ffprobeExists);
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
            "test");

        return videoPath;
    }

    private static ExternalProcessResult
        CreateCompletedProcessResult(
            int exitCode = 0,
            string standardOutput = """{"streams":[]}""",
            string standardError = "")
    {
        return new ExternalProcessResult(
            ExternalProcessRunStatus.Completed,
            exitCode,
            standardOutput,
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

    private sealed class FakeExternalProcessRunner(
        ExternalProcessResult result)
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

            return Task.FromResult(result);
        }
    }
}
