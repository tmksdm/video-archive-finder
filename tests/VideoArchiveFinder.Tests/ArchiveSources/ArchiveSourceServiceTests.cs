using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Tests.ArchiveSources;

public sealed class ArchiveSourceServiceTests
{
    [Fact]
    public async Task AddAsync_NewSource_AddsAndSavesSource()
    {
        var store = new TestArchiveSourceStore();
        using var service = new ArchiveSourceService(store);

        var result = await service.AddAsync(
            @"C:\Video Archive",
            "Local archive");

        Assert.True(result.WasAdded);
        Assert.Single(store.Sources);
        Assert.Equal(result.Source, store.Sources[0]);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task AddAsync_ExactDuplicate_ReturnsExistingSource()
    {
        var existingSource = ArchiveSource.Create(
            @"C:\Video Archive");

        var store = new TestArchiveSourceStore(existingSource);
        using var service = new ArchiveSourceService(store);

        var result = await service.AddAsync(
            @"C:\Video Archive");

        Assert.False(result.WasAdded);
        Assert.Equal(existingSource, result.Source);
        Assert.Single(store.Sources);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task AddAsync_DuplicateWithDifferentCase_DoesNotAddSource()
    {
        var existingSource = ArchiveSource.Create(
            @"C:\Video Archive");

        var store = new TestArchiveSourceStore(existingSource);
        using var service = new ArchiveSourceService(store);

        var result = await service.AddAsync(
            @"c:\video archive");

        Assert.False(result.WasAdded);
        Assert.Equal(existingSource, result.Source);
        Assert.Single(store.Sources);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task AddAsync_DuplicateWithDifferentSeparators_DoesNotAddSource()
    {
        var existingSource = ArchiveSource.Create(
            @"C:\Video Archive\News");

        var store = new TestArchiveSourceStore(existingSource);
        using var service = new ArchiveSourceService(store);

        var result = await service.AddAsync(
            "C:/Video Archive/News/");

        Assert.False(result.WasAdded);
        Assert.Equal(existingSource, result.Source);
        Assert.Single(store.Sources);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task RemoveAsync_ExistingSource_RemovesAndSavesSource()
    {
        var source = ArchiveSource.Create(
            @"C:\Video Archive");

        var store = new TestArchiveSourceStore(source);
        using var service = new ArchiveSourceService(store);

        var wasRemoved = await service.RemoveAsync(source.Id);

        Assert.True(wasRemoved);
        Assert.Empty(store.Sources);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task RemoveAsync_UnknownSource_DoesNotSave()
    {
        var source = ArchiveSource.Create(
            @"C:\Video Archive");

        var store = new TestArchiveSourceStore(source);
        using var service = new ArchiveSourceService(store);

        var wasRemoved = await service.RemoveAsync(Guid.NewGuid());

        Assert.False(wasRemoved);
        Assert.Single(store.Sources);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task RemoveAsync_ExistingSource_PreservesOtherSources()
    {
        var firstSource = ArchiveSource.Create(
            @"C:\First Archive");

        var secondSource = ArchiveSource.Create(
            @"D:\Second Archive");

        var store = new TestArchiveSourceStore(
            firstSource,
            secondSource);

        using var service = new ArchiveSourceService(store);

        var wasRemoved = await service.RemoveAsync(
            firstSource.Id);

        Assert.True(wasRemoved);
        Assert.Single(store.Sources);
        Assert.Equal(secondSource, store.Sources[0]);
        Assert.Equal(1, store.SaveCallCount);
    }

    private sealed class TestArchiveSourceStore(
        params ArchiveSource[] initialSources)
        : IArchiveSourceStore
    {
        private IReadOnlyList<ArchiveSource> _sources =
            initialSources.ToList();

        public IReadOnlyList<ArchiveSource> Sources => _sources;

        public int SaveCallCount { get; private set; }

        public Task<IReadOnlyList<ArchiveSource>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<ArchiveSource>>(
                _sources.ToList());
        }

        public Task SaveAsync(
            IEnumerable<ArchiveSource> sources,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _sources = sources.ToList();
            SaveCallCount++;

            return Task.CompletedTask;
        }
    }
}
