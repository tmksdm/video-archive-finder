using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Domain.ArchiveSources;
using VideoArchiveFinder.Application.Search;


namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class FolderIndexingService
    : IFolderIndexingService
{
    private const int BatchSize = 250;

    private readonly IFolderTreeEnumerator
        _folderTreeEnumerator;

    private readonly IFolderIndexRepository
        _folderIndexRepository;

    private readonly IFolderIndexingStateRepository
        _folderIndexingStateRepository;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ISearchStemService
        _searchStemService;

    private readonly ILogger<FolderIndexingService>
        _logger;

    public FolderIndexingService(
        IFolderTreeEnumerator folderTreeEnumerator,
        IFolderIndexRepository folderIndexRepository,
IFolderIndexingStateRepository
    folderIndexingStateRepository,
ITextNormalizationService
    textNormalizationService,
ISearchStemService
    searchStemService,
ILogger<FolderIndexingService> logger)

    {
        _folderTreeEnumerator =
            folderTreeEnumerator;

        _folderIndexRepository =
            folderIndexRepository;

        _folderIndexingStateRepository =
            folderIndexingStateRepository;

        _textNormalizationService =
            textNormalizationService;

        _searchStemService =
            searchStemService;

        _logger = logger;
    }

    public Task<FolderIndexingResult> ScanAsync(
        ArchiveSource source,
        IProgress<FolderIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Task.Run(
            () => ScanCoreAsync(
                source,
                progress,
                cancellationToken),
            cancellationToken);
    }

    private async Task<FolderIndexingResult> ScanCoreAsync(
        ArchiveSource source,
        IProgress<FolderIndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        var discoveredFolderCount = 0;
        var indexedFolderCount = 0;
        var errorCount = 0;

        var batch =
            new List<FolderIndexUpsertItem>(
                BatchSize);

        var protectedPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "Folder indexing started for source " +
            "{SourceId} at {SourcePath}.",
            source.Id,
            source.FullPath);

        try
        {
            await foreach (var entry in
                _folderTreeEnumerator
                    .EnumerateAsync(
                        source.FullPath,
                        cancellationToken)
                    .WithCancellation(
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (entry is FolderEnumerationError error)
                {
                    errorCount++;

                    protectedPaths.Add(
                        error.DirectoryPath);

                    _logger.LogWarning(
                        error.Exception,
                        "Cannot enumerate directory {DirectoryPath}. " +
                        "Scanning will continue.",
                        error.DirectoryPath);

                    ReportProgress(
                        progress,
                        FolderIndexingStage.Enumerating,
                        error.DirectoryPath,
                        discoveredFolderCount,
                        indexedFolderCount,
                        errorCount);

                    continue;
                }

                if (entry is not DiscoveredFolder folder)
                {
                    continue;
                }

                discoveredFolderCount++;

                batch.Add(
                    CreateIndexItem(
                        folder,
                        source.Id,
                        startedAtUtc));

                ReportProgress(
                    progress,
                    FolderIndexingStage.Enumerating,
                    folder.FullPath,
                    discoveredFolderCount,
                    indexedFolderCount,
                    errorCount);

                if (batch.Count < BatchSize)
                {
                    continue;
                }

                indexedFolderCount +=
                    await WriteBatchAsync(
                            batch,
                            progress,
                            folder.FullPath,
                            discoveredFolderCount,
                            indexedFolderCount,
                            errorCount,
                            cancellationToken)
                        .ConfigureAwait(false);
            }

            if (batch.Count > 0)
            {
                indexedFolderCount +=
                    await WriteBatchAsync(
                            batch,
                            progress,
                            CurrentPath: null,
                            discoveredFolderCount,
                            indexedFolderCount,
                            errorCount,
                            cancellationToken)
                        .ConfigureAwait(false);
            }

            var removedFolderCount =
                await _folderIndexRepository
                    .CompleteScanAsync(
                        source.Id,
                        startedAtUtc,
                        protectedPaths,
                        cancellationToken)
                    .ConfigureAwait(false);

            var completedAtUtc =
                DateTimeOffset.UtcNow;

            var result =
                new FolderIndexingResult(
                    RootSourceId:
                        source.Id,
                    DiscoveredFolderCount:
                        discoveredFolderCount,
                    IndexedFolderCount:
                        indexedFolderCount,
                    ErrorCount:
                        errorCount,
                    StartedAtUtc:
                        startedAtUtc,
                    CompletedAtUtc:
                        completedAtUtc);

            await _folderIndexingStateRepository
                .SaveAsync(
                    new FolderIndexingState(
                        RootSourceId:
                            result.RootSourceId,
                        DiscoveredFolderCount:
                            result.DiscoveredFolderCount,
                        IndexedFolderCount:
                            result.IndexedFolderCount,
                        ErrorCount:
                            result.ErrorCount,
                        StartedAtUtc:
                            result.StartedAtUtc,
                        CompletedAtUtc:
                            result.CompletedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            ReportProgress(
                progress,
                FolderIndexingStage.Completed,
                CurrentPath: null,
                discoveredFolderCount,
                indexedFolderCount,
                errorCount);

            _logger.LogInformation(
                "Folder indexing completed for source {SourceId}. " +
                "Discovered: {DiscoveredFolderCount}; " +
                "indexed: {IndexedFolderCount}; errors: {ErrorCount}; " +
                "removed stale folders: {RemovedFolderCount}.",
                source.Id,
                discoveredFolderCount,
                indexedFolderCount,
                errorCount,
                removedFolderCount);

            return result;

        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Folder indexing was cancelled for source {SourceId}.",
                source.Id);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Folder indexing failed for source {SourceId} " +
                "at {SourcePath}.",
                source.Id,
                source.FullPath);

            throw;
        }
    }

    private async Task<int> WriteBatchAsync(
        List<FolderIndexUpsertItem> batch,
        IProgress<FolderIndexingProgress>? progress,
        string? CurrentPath,
        int discoveredFolderCount,
        int indexedFolderCount,
        int errorCount,
        CancellationToken cancellationToken)
    {
        var itemsToWrite = batch.ToArray();

        ReportProgress(
            progress,
            FolderIndexingStage.WritingBatch,
            CurrentPath,
            discoveredFolderCount,
            indexedFolderCount,
            errorCount);

        await _folderIndexRepository
            .UpsertBatchAsync(
                itemsToWrite,
                cancellationToken)
            .ConfigureAwait(false);

        batch.Clear();

        return itemsToWrite.Length;
    }

    private FolderIndexUpsertItem CreateIndexItem(
        DiscoveredFolder folder,
        Guid rootSourceId,
        DateTimeOffset lastSeenUtc)
    {
        var normalizedName =
            _textNormalizationService.Normalize(
                folder.Name);

        var searchTokens =
            _textNormalizationService.Tokenize(
                folder.Name);

        return new FolderIndexUpsertItem(
            FullPath:
                folder.FullPath,
            Name:
                folder.Name,
            NormalizedName:
                normalizedName,
            SearchTokens:
                string.Join(' ', searchTokens),
            SearchStems:
                _searchStemService.CreateStemText(
                    searchTokens),
            ParentFullPath:
                folder.ParentFullPath,
            RootSourceId:
                rootSourceId,
            IsAvailable:
                folder.IsAvailable,
            LastSeenUtc:
                lastSeenUtc,
            DirectSubfolderCount:
                folder.DirectSubfolderCount,
            DirectVideoFileCount:
                0);
    }


    private static void ReportProgress(
        IProgress<FolderIndexingProgress>? progress,
        FolderIndexingStage stage,
        string? CurrentPath,
        int discoveredFolderCount,
        int indexedFolderCount,
        int errorCount)
    {
        progress?.Report(
            new FolderIndexingProgress(
                Stage: stage,
                CurrentPath: CurrentPath,
                DiscoveredFolderCount:
                    discoveredFolderCount,
                IndexedFolderCount:
                    indexedFolderCount,
                ErrorCount:
                    errorCount));
    }
}
