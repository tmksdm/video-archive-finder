using VideoArchiveFinder.Application.ExternalTools;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class FfprobeRunner
    : IFfprobeRunner
{
    private static readonly TimeSpan DefaultTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IFfmpegToolsLocator _toolsLocator;
    private readonly IExternalProcessRunner _processRunner;

    public FfprobeRunner(
        IFfmpegToolsLocator toolsLocator,
        IExternalProcessRunner processRunner)
    {
        _toolsLocator = toolsLocator;
        _processRunner = processRunner;
    }

    public async Task<FfprobeRunResult> RunAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            videoPath);

        cancellationToken.ThrowIfCancellationRequested();

        var toolsStatus = _toolsLocator.Locate();

        if (!toolsStatus.FfprobeExists)
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.ToolUnavailable,
                string.Empty,
                null,
                $"FFprobe не найден. Ожидаемый файл: " +
                $"{toolsStatus.FfprobePath}");
        }

        if (!File.Exists(videoPath))
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.InputUnavailable,
                string.Empty,
                null,
                $"Видеофайл недоступен: {videoPath}");
        }

        var request = new ExternalProcessRequest(
            toolsStatus.FfprobePath,
            CreateArguments(videoPath),
            DefaultTimeout);

        var processResult = await _processRunner.RunAsync(
            request,
            cancellationToken);

        return MapResult(processResult);
    }

    private static IReadOnlyList<string> CreateArguments(
        string videoPath)
    {
        return
        [
            "-v",
            "error",
            "-print_format",
            "json",
            "-show_entries",
            "format=duration:" +
            "stream=codec_type,codec_name,width,height",
            videoPath
        ];
    }

    private static FfprobeRunResult MapResult(
        ExternalProcessResult processResult)
    {
        if (processResult.Status ==
            ExternalProcessRunStatus.TimedOut)
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.TimedOut,
                processResult.StandardOutput,
                processResult.ExitCode,
                processResult.DiagnosticMessage);
        }

        if (processResult.Status ==
            ExternalProcessRunStatus.FailedToStart)
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.Failed,
                processResult.StandardOutput,
                processResult.ExitCode,
                processResult.DiagnosticMessage);
        }

        if (processResult.ExitCode != 0)
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.Failed,
                processResult.StandardOutput,
                processResult.ExitCode,
                CreateExitCodeMessage(processResult));
        }

        if (string.IsNullOrWhiteSpace(
            processResult.StandardOutput))
        {
            return new FfprobeRunResult(
                FfprobeRunStatus.Failed,
                string.Empty,
                processResult.ExitCode,
                "FFprobe завершился без JSON-результата.");
        }

        return new FfprobeRunResult(
            FfprobeRunStatus.Succeeded,
            processResult.StandardOutput,
            processResult.ExitCode,
            "FFprobe успешно завершён.");
    }

    private static string CreateExitCodeMessage(
        ExternalProcessResult processResult)
    {
        var message =
            $"FFprobe завершился с кодом " +
            $"{processResult.ExitCode}.";

        if (string.IsNullOrWhiteSpace(
            processResult.StandardError))
        {
            return message;
        }

        return $"{message} " +
               processResult.StandardError.Trim();
    }
}
