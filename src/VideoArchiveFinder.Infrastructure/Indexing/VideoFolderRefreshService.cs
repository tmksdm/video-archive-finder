using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class VideoFolderRefreshService
    : IVideoFolderRefreshService
{
    private readonly IVideoFileDiscoveryService
        _videoFileDiscoveryService;

    private readonly IVideoFileIndexRepository
        _videoFileIndexRepository;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ILogger<VideoFolderRefreshService>
        _logger;

    public VideoFolderRefreshService(
        IVideoFileDiscoveryService videoFileDiscoveryService,
        IVideoFileIndexRepository videoFileIndexRepository,
        ITextNormalizationService textNormalizationService,
        ILogger<VideoFolderRefreshService> logger)
    {
        _videoFileDiscoveryService =
            videoFileDiscoveryService;

        _videoFileIndexRepository =
            videoFileIndexRepository;

        _textNormalizationService =
            textNormalizationService;

        _logger = logger;
    }

    public async Task<VideoFolderRefreshResult> RefreshAsync(
        Guid rootSourceId,
        string folderFullPath,
        CancellationToken cancellationToken = default)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        if (string.IsNullOrWhiteSpace(folderFullPath))
        {
            throw new ArgumentException(
                "Folder path cannot be empty.",
                nameof(folderFullPath));
        }

        var scanStartedAtUtc = DateTimeOffset.UtcNow;

        var discoveryResult =
            await _videoFileDiscoveryService
                .DiscoverAsync(
                    folderFullPath,
                    cancellationToken)
                .ConfigureAwait(false);

        var files =
            discoveryResult.Files
                .Select(
                    file =>
                        CreateIndexItem(
                            file,
                            rootSourceId,
                            folderFullPath,
                            scanStartedAtUtc))
                .ToArray();

        await _videoFileIndexRepository
            .UpsertBatchAsync(
                files,
                cancellationToken)
            .ConfigureAwait(false);

        if (discoveryResult.CanRemoveStaleEntries)
        {
            await _videoFileIndexRepository
                .CompleteFolderScanAsync(
                    rootSourceId,
                    folderFullPath,
                    scanStartedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Video list refresh for {FolderPath} was incomplete. " +
                "Cached entries were preserved.",
                folderFullPath);
        }

        var indexedFiles =
            await _videoFileIndexRepository
                .GetByFolderPathAsync(
                    rootSourceId,
                    folderFullPath,
                    cancellationToken)
                .ConfigureAwait(false);

        return new VideoFolderRefreshResult(
            indexedFiles,
            discoveryResult.ErrorCount,
            discoveryResult.CanRemoveStaleEntries);
    }

    private VideoFileIndexUpsertItem CreateIndexItem(
        DiscoveredVideoFile file,
        Guid rootSourceId,
        string folderFullPath,
        DateTimeOffset lastSeenUtc)
    {
        return new VideoFileIndexUpsertItem(
            FullPath: file.FullPath,
            Name: file.Name,
            NormalizedName:
                _textNormalizationService.Normalize(
                    file.Name),
            Extension: file.Extension,
            SizeBytes: file.SizeBytes,
            LastWriteTimeUtc: file.LastWriteTimeUtc,
            FolderFullPath: folderFullPath,
            RootSourceId: rootSourceId,
            IsAvailable: true,
            LastSeenUtc: lastSeenUtc);
    }
}
