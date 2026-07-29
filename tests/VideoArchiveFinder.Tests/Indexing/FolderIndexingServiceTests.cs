using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Domain.ArchiveSources;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Application.Search;


namespace VideoArchiveFinder.Tests.Indexing;

public sealed class FolderIndexingServiceTests
{
    [Fact]
    public async Task ScanAsync_IndexesFoldersAndContinuesAfterError()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var accessException =
            new UnauthorizedAccessException(
                "Access denied.");

        var enumerator =
            new TestFolderTreeEnumerator(
            [
                new DiscoveredFolder(
                    FullPath: @"C:\Archive",
                    Name: "Archive",
                    ParentFullPath: null,
                    DirectSubfolderCount: 2,
                    IsAvailable: true,
                    IsReparsePoint: false),

                new FolderEnumerationError(
                    DirectoryPath:
                        @"C:\Archive\Закрытая папка",
                    Exception:
                        accessException),

                new DiscoveredFolder(
                    FullPath:
                        @"C:\Archive\ЁЖ_Дорога",
                    Name:
                        "ЁЖ_Дорога",
                    ParentFullPath:
                        @"C:\Archive",
                    DirectSubfolderCount: 0,
                    IsAvailable: true,
                    IsReparsePoint: false)
            ]);

        var repository =
            new RecordingFolderIndexRepository();

        var stateRepository =
            new RecordingFolderIndexingStateRepository();

        var progress =
            new RecordingProgress();

        var service =
            CreateService(
                enumerator,
                repository,
                stateRepository);


        var result =
            await service.ScanAsync(
                source,
                progress);

        Assert.Equal(source.Id, result.RootSourceId);
        Assert.Equal(2, result.DiscoveredFolderCount);
        Assert.Equal(2, result.IndexedFolderCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.True(
            result.CompletedAtUtc >=
            result.StartedAtUtc);

        var savedState =
            Assert.Single(
                stateRepository.SavedStates);

        Assert.Equal(
            result.RootSourceId,
            savedState.RootSourceId);

        Assert.Equal(
            result.DiscoveredFolderCount,
            savedState.DiscoveredFolderCount);

        Assert.Equal(
            result.IndexedFolderCount,
            savedState.IndexedFolderCount);

        Assert.Equal(
            result.ErrorCount,
            savedState.ErrorCount);

        Assert.Equal(
            result.StartedAtUtc,
            savedState.StartedAtUtc);

        Assert.Equal(
            result.CompletedAtUtc,
            savedState.CompletedAtUtc);


        var batch =
            Assert.Single(repository.Batches);

        Assert.Equal(2, batch.Count);

        var indexedFolder =
            Assert.Single(
                batch,
                item => item.FullPath ==
                    @"C:\Archive\ЁЖ_Дорога");

        Assert.Equal(
            "еж_дорога",
            indexedFolder.NormalizedName);

        Assert.Equal(
            "еж дорога",
            indexedFolder.SearchTokens);

        Assert.Equal(
            "еж дорог",
            indexedFolder.SearchStems);


        Assert.Equal(
            @"C:\Archive",
            indexedFolder.ParentFullPath);

        Assert.Equal(
            source.Id,
            indexedFolder.RootSourceId);

        Assert.True(indexedFolder.IsAvailable);
        Assert.Equal(
            0,
            indexedFolder.DirectVideoFileCount);

        Assert.Contains(
            progress.Reports,
            report =>
                report.ErrorCount == 1);

        Assert.Equal(
            FolderIndexingStage.Completed,
            progress.Reports[^1].Stage);

        var completion =
            Assert.Single(repository.Completions);

        Assert.Equal(source.Id, completion.RootSourceId);
        Assert.Contains(
            @"C:\Archive\Закрытая папка",
            completion.ProtectedPaths);
    }

    [Fact]
    public async Task ScanAsync_MoreThanBatchSize_WritesSeveralBatches()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var entries =
            Enumerable.Range(0, 251)
                .Select(
                    index =>
                        (FolderEnumerationEntry)
                        new DiscoveredFolder(
                            FullPath:
                                $@"C:\Archive\Folder{index}",
                            Name:
                                $"Folder{index}",
                            ParentFullPath:
                                @"C:\Archive",
                            DirectSubfolderCount: 0,
                            IsAvailable: true,
                            IsReparsePoint: false))
                .ToArray();

        var repository =
            new RecordingFolderIndexRepository();

        var service =
            CreateService(
                new TestFolderTreeEnumerator(
                    entries),
                repository);

        var result =
            await service.ScanAsync(source);

        Assert.Equal(251, result.IndexedFolderCount);
        Assert.Equal(2, repository.Batches.Count);
        Assert.Equal(250, repository.Batches[0].Count);
        Assert.Single(repository.Batches[1]);
        Assert.Single(repository.Completions);
    }

    [Fact]
    public async Task ScanAsync_WhenCancelled_ThrowsCancellation()
    {
        var source =
            ArchiveSource.Create(@"C:\Archive");

        var repository =
            new RecordingFolderIndexRepository();

        var stateRepository =
            new RecordingFolderIndexingStateRepository();

        var service =
            CreateService(
                new BlockingFolderTreeEnumerator(),
                repository,
                stateRepository);


        using var cancellationSource =
            new CancellationTokenSource();

        var scanningTask =
            service.ScanAsync(
                source,
                cancellationToken:
                    cancellationSource.Token);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
                () => scanningTask);

        Assert.Empty(repository.Batches);
        Assert.Empty(repository.Completions);
        Assert.Empty(stateRepository.SavedStates);
    }

    private static FolderIndexingService CreateService(
        IFolderTreeEnumerator enumerator,
        IFolderIndexRepository repository,
        IFolderIndexingStateRepository?
            stateRepository = null)
    {
        return new FolderIndexingService(
            enumerator,
            repository,
            stateRepository ??
                new RecordingFolderIndexingStateRepository(),
            new TextNormalizationService(),
            new RussianSearchStemService(),
            NullLogger<FolderIndexingService>.Instance);
    }



    private sealed class TestFolderTreeEnumerator
        : IFolderTreeEnumerator
    {
        private readonly
            IReadOnlyList<FolderEnumerationEntry>
            _entries;

        public TestFolderTreeEnumerator(
            IReadOnlyList<FolderEnumerationEntry> entries)
        {
            _entries = entries;
        }

        public async IAsyncEnumerable<
            FolderEnumerationEntry> EnumerateAsync(
                string rootPath,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            foreach (var entry in _entries)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                await Task.Yield();

                yield return entry;
            }
        }
    }

    private sealed class BlockingFolderTreeEnumerator
        : IFolderTreeEnumerator
    {
        public async IAsyncEnumerable<
            FolderEnumerationEntry> EnumerateAsync(
                string rootPath,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            yield break;
        }
    }

    private sealed class RecordingFolderIndexRepository
        : IFolderIndexRepository
    {
        public List<
            IReadOnlyList<FolderIndexUpsertItem>>
            Batches
        { get; } = [];

        public List<ScanCompletion>
            Completions
        { get; } = [];

        public Task UpsertBatchAsync(
            IReadOnlyCollection<
                FolderIndexUpsertItem> folders,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Batches.Add(folders.ToArray());

            return Task.CompletedTask;
        }

        public Task<int> CompleteScanAsync(
            Guid rootSourceId,
            DateTimeOffset scanStartedAtUtc,
            IReadOnlyCollection<string> protectedPaths,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Completions.Add(
                new ScanCompletion(
                    rootSourceId,
                    scanStartedAtUtc,
                    protectedPaths.ToArray()));

            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<IndexedFolder>>
            GetByRootSourceIdAsync(
                Guid rootSourceId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<IndexedFolder>>([]);
        }
    }

    private sealed record ScanCompletion(
        Guid RootSourceId,
        DateTimeOffset ScanStartedAtUtc,
        IReadOnlyList<string> ProtectedPaths);

    private sealed class RecordingFolderIndexingStateRepository
        : IFolderIndexingStateRepository
    {
        public List<FolderIndexingState> SavedStates
        {
            get;
        } = [];

        public Task SaveAsync(
            FolderIndexingState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            SavedStates.Add(state);

            return Task.CompletedTask;
        }

        public Task<FolderIndexingState?> GetAsync(
            Guid rootSourceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            FolderIndexingState? state =
                SavedStates.LastOrDefault(
                    item =>
                        item.RootSourceId ==
                        rootSourceId);

            return Task.FromResult(state);
        }
    }


    private sealed class RecordingProgress
        : IProgress<FolderIndexingProgress>
    {
        public List<FolderIndexingProgress>
            Reports
        { get; } = [];

        public void Report(
            FolderIndexingProgress value)
        {
            Reports.Add(value);
        }
    }
}
