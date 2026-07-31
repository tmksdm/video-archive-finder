using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class VideoFileDiscoveryService
    : IVideoFileDiscoveryService
{
    private readonly IVideoFileSystem
        _videoFileSystem;

    private readonly IVideoFileCandidatePolicy
        _videoFileCandidatePolicy;

    private readonly ILogger<VideoFileDiscoveryService>
        _logger;

    public VideoFileDiscoveryService(
        IVideoFileSystem videoFileSystem,
        IVideoFileCandidatePolicy videoFileCandidatePolicy,
        ILogger<VideoFileDiscoveryService> logger)
    {
        _videoFileSystem = videoFileSystem;
        _videoFileCandidatePolicy =
            videoFileCandidatePolicy;
        _logger = logger;
    }

    public Task<VideoFileDiscoveryResult> DiscoverAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException(
                "Folder path cannot be empty.",
                nameof(folderPath));
        }

        return Task.Run(
            () => DiscoverCore(
                folderPath,
                cancellationToken),
            cancellationToken);
    }

    private VideoFileDiscoveryResult DiscoverCore(
        string folderPath,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> filePaths;

        try
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            filePaths =
                _videoFileSystem.GetFiles(
                    folderPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Cannot enumerate files in folder {FolderPath}.",
                folderPath);

            return new VideoFileDiscoveryResult(
                Files: [],
                ErrorCount: 1,
                CanRemoveStaleEntries: false);
        }

        var discoveredFiles =
            new List<DiscoveredVideoFile>();

        var errorCount = 0;
        var canRemoveStaleEntries = true;

        foreach (var filePath in filePaths)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                if (!_videoFileCandidatePolicy
                    .IsCandidate(filePath))
                {
                    continue;
                }

                var metadata =
                    _videoFileSystem.GetMetadata(
                        filePath);

                var fileName =
                    Path.GetFileName(filePath);

                var extension =
                    Path.GetExtension(filePath)
                        .ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(fileName) ||
                    string.IsNullOrWhiteSpace(extension))
                {
                    throw new InvalidOperationException(
                        "Не удалось определить имя или " +
                        "расширение файла.");
                }

                discoveredFiles.Add(
                    new DiscoveredVideoFile(
                        FullPath: filePath,
                        Name: fileName,
                        Extension: extension,
                        SizeBytes:
                            metadata.SizeBytes,
                        LastWriteTimeUtc:
                            metadata.LastWriteTimeUtc));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errorCount++;
                canRemoveStaleEntries = false;

                _logger.LogWarning(
                    exception,
                    "Cannot inspect video file candidate " +
                    "{FilePath}. Scanning will continue.",
                    filePath);
            }
        }

        return new VideoFileDiscoveryResult(
            Files: discoveredFiles,
            ErrorCount: errorCount,
            CanRemoveStaleEntries:
                canRemoveStaleEntries);
    }
}
