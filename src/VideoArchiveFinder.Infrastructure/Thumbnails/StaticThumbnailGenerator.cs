using System.Globalization;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class StaticThumbnailGenerator
    : IStaticThumbnailGenerator
{
    private static readonly TimeSpan GenerationTimeout =
        TimeSpan.FromMinutes(1);

    private readonly IFfmpegToolsLocator _toolsLocator;
    private readonly IExternalProcessRunner _processRunner;
    private readonly IApplicationDataDirectoryProvider
        _applicationDataDirectoryProvider;
    private readonly IThumbnailCacheKeyGenerator
        _cacheKeyGenerator;
    private readonly ILogger<StaticThumbnailGenerator> _logger;

    public StaticThumbnailGenerator(
        IFfmpegToolsLocator toolsLocator,
        IExternalProcessRunner processRunner,
        IApplicationDataDirectoryProvider
            applicationDataDirectoryProvider,
        IThumbnailCacheKeyGenerator cacheKeyGenerator,
        ILogger<StaticThumbnailGenerator> logger)
    {
        _toolsLocator = toolsLocator;
        _processRunner = processRunner;
        _applicationDataDirectoryProvider =
            applicationDataDirectoryProvider;
        _cacheKeyGenerator = cacheKeyGenerator;
        _logger = logger;
    }

    public async Task<StaticThumbnailGenerationResult>
        GenerateAsync(
            StaticThumbnailRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.VideoPath);

        ArgumentOutOfRangeException.ThrowIfNegative(
            request.SizeBytes);

        cancellationToken.ThrowIfCancellationRequested();

        var toolsStatus = _toolsLocator.Locate();

        if (!toolsStatus.FfmpegExists)
        {
            return CreateResult(
                StaticThumbnailGenerationStatus.ToolUnavailable,
                null,
                null,
                $"FFmpeg не найден. Ожидаемый файл: " +
                $"{toolsStatus.FfmpegPath}");
        }

        if (!File.Exists(request.VideoPath))
        {
            return CreateResult(
                StaticThumbnailGenerationStatus.InputUnavailable,
                null,
                null,
                $"Видеофайл недоступен: {request.VideoPath}");
        }

        string? temporaryPath = null;

        try
        {
            var thumbnailPath = CreateThumbnailPath(
                request);

            if (IsUsableFile(thumbnailPath))
            {
                return CreateResult(
                    StaticThumbnailGenerationStatus.CacheHit,
                    thumbnailPath,
                    null,
                    "Готовая миниатюра найдена в кэше.");
            }

            var thumbnailDirectory =
                Path.GetDirectoryName(thumbnailPath) ??
                throw new InvalidOperationException(
                    "Не удалось определить папку миниатюры.");

            Directory.CreateDirectory(
                thumbnailDirectory);

            temporaryPath = CreateTemporaryPath(
                thumbnailPath);

            var processRequest =
                new ExternalProcessRequest(
                    toolsStatus.FfmpegPath,
                    CreateArguments(
                        request.VideoPath,
                        temporaryPath),
                    GenerationTimeout);

            var processResult =
                await _processRunner.RunAsync(
                    processRequest,
                    cancellationToken);

            var failedResult = MapFailedProcessResult(
                processResult);

            if (failedResult is not null)
            {
                return failedResult;
            }

            if (!IsUsableFile(temporaryPath))
            {
                return CreateResult(
                    StaticThumbnailGenerationStatus.Failed,
                    null,
                    processResult.ExitCode,
                    "FFmpeg завершился без создания " +
                    "корректной миниатюры.");
            }

            File.Move(
                temporaryPath,
                thumbnailPath,
                overwrite: true);

            temporaryPath = null;

            return CreateResult(
                StaticThumbnailGenerationStatus.Generated,
                thumbnailPath,
                processResult.ExitCode,
                "Статическая миниатюра успешно создана.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Не удалось создать статическую миниатюру " +
                "для {VideoPath}.",
                request.VideoPath);

            return CreateResult(
                StaticThumbnailGenerationStatus.Failed,
                null,
                null,
                $"Ошибка файловой системы: " +
                $"{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Недостаточно прав для создания миниатюры " +
                "для {VideoPath}.",
                request.VideoPath);

            return CreateResult(
                StaticThumbnailGenerationStatus.Failed,
                null,
                null,
                $"Недостаточно прав для записи кэша: " +
                $"{exception.Message}");
        }
        finally
        {
            TryDeleteTemporaryFile(
                temporaryPath,
                request.VideoPath);
        }
    }

    private string CreateThumbnailPath(
        StaticThumbnailRequest request)
    {
        var key = _cacheKeyGenerator.GenerateKey(
            request);

        var cacheDirectory = Path.Combine(
            _applicationDataDirectoryProvider
                .GetApplicationDataDirectory(),
            "Cache",
            "Thumbnails",
            $"v{StaticThumbnailProfile.CacheFormatVersion}",
            key[..2]);

        return Path.Combine(
            cacheDirectory,
            $"{key}.jpg");
    }

    private static string CreateTemporaryPath(
        string thumbnailPath)
    {
        return $"{thumbnailPath}." +
               $"{Guid.NewGuid():N}.tmp.jpg";
    }

    private static IReadOnlyList<string> CreateArguments(
        string videoPath,
        string outputPath)
    {
        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin",
            "-y",
            "-ss",
            StaticThumbnailProfile
                .SeekPosition
                .TotalSeconds
                .ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
            "-i",
            videoPath,
            "-frames:v",
            "1",
            "-vf",
            $"scale={StaticThumbnailProfile.OutputWidth}:" +
            "-2:force_original_aspect_ratio=decrease",
            "-q:v",
            StaticThumbnailProfile
                .JpegQuality
                .ToString(CultureInfo.InvariantCulture),
            outputPath
        ];
    }

    private static StaticThumbnailGenerationResult?
        MapFailedProcessResult(
            ExternalProcessResult processResult)
    {
        if (processResult.Status ==
            ExternalProcessRunStatus.TimedOut)
        {
            return CreateResult(
                StaticThumbnailGenerationStatus.TimedOut,
                null,
                processResult.ExitCode,
                processResult.DiagnosticMessage);
        }

        if (processResult.Status ==
            ExternalProcessRunStatus.FailedToStart)
        {
            return CreateResult(
                StaticThumbnailGenerationStatus.Failed,
                null,
                processResult.ExitCode,
                processResult.DiagnosticMessage);
        }

        if (processResult.ExitCode != 0)
        {
            return CreateResult(
                StaticThumbnailGenerationStatus.Failed,
                null,
                processResult.ExitCode,
                CreateExitCodeMessage(processResult));
        }

        return null;
    }

    private static string CreateExitCodeMessage(
        ExternalProcessResult processResult)
    {
        var message =
            $"FFmpeg завершился с кодом " +
            $"{processResult.ExitCode}.";

        if (string.IsNullOrWhiteSpace(
            processResult.StandardError))
        {
            return message;
        }

        return $"{message} " +
               processResult.StandardError.Trim();
    }

    private bool IsUsableFile(
        string filePath)
    {
        try
        {
            var file = new FileInfo(filePath);

            return file.Exists &&
                   file.Length > 0;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Не удалось проверить файл миниатюры " +
                "{ThumbnailPath}.",
                filePath);

            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Нет доступа к файлу миниатюры " +
                "{ThumbnailPath}.",
                filePath);

            return false;
        }
    }

    private void TryDeleteTemporaryFile(
        string? temporaryPath,
        string videoPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryPath))
        {
            return;
        }

        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Не удалось удалить временную миниатюру " +
                "{TemporaryPath} для {VideoPath}.",
                temporaryPath,
                videoPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Нет доступа для удаления временной " +
                "миниатюры {TemporaryPath} для {VideoPath}.",
                temporaryPath,
                videoPath);
        }
    }

    private static StaticThumbnailGenerationResult
        CreateResult(
            StaticThumbnailGenerationStatus status,
            string? thumbnailPath,
            int? exitCode,
            string diagnosticMessage)
    {
        return new StaticThumbnailGenerationResult(
            status,
            thumbnailPath,
            exitCode,
            diagnosticMessage);
    }
}
