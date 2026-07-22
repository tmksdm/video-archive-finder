using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Application.ArchiveSources;

public sealed class ArchiveSourceService :
    IArchiveSourceService,
    IDisposable
{
    private readonly IArchiveSourceStore _store;
    private readonly SemaphoreSlim _accessLock = new(1, 1);

    public ArchiveSourceService(IArchiveSourceStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<ArchiveSource>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await _store.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public async Task<AddArchiveSourceResult> AddAsync(
        string fullPath,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var newSource = ArchiveSource.Create(
            fullPath,
            displayName);

        await _accessLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var sources = (await _store.LoadAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();

            var existingSource = sources.FirstOrDefault(
                source => PathsAreEquivalent(
                    source.FullPath,
                    newSource.FullPath));

            if (existingSource is not null)
            {
                return new AddArchiveSourceResult(
                    existingSource,
                    WasAdded: false);
            }

            sources.Add(newSource);

            await _store.SaveAsync(
                    sources,
                    cancellationToken)
                .ConfigureAwait(false);

            return new AddArchiveSourceResult(
                newSource,
                WasAdded: true);
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public Task<bool> RemoveAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return RemoveManyAsync(
            [sourceId],
            cancellationToken);
    }

    public async Task<bool> RemoveManyAsync(
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        if (sourceIds.Count == 0 ||
            sourceIds.Any(sourceId => sourceId == Guid.Empty))
        {
            return false;
        }

        var sourceIdsToRemove = sourceIds.ToHashSet();

        await _accessLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var sources = (await _store.LoadAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();

            var existingSourceIds = sources
                .Select(source => source.Id)
                .ToHashSet();

            if (!sourceIdsToRemove.IsSubsetOf(existingSourceIds))
            {
                return false;
            }

            sources.RemoveAll(
                source => sourceIdsToRemove.Contains(source.Id));

            await _store.SaveAsync(
                    sources,
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public void Dispose()
    {
        _accessLock.Dispose();
    }

    private static bool PathsAreEquivalent(
        string firstPath,
        string secondPath)
    {
        return string.Equals(
            NormalizeForComparison(firstPath),
            NormalizeForComparison(secondPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForComparison(string path)
    {
        var normalizedPath = path
            .Replace('/', '\\')
            .TrimEnd('\\');

        return normalizedPath.Length == 2
            && normalizedPath[1] == ':'
                ? $"{normalizedPath}\\"
                : normalizedPath;
    }
}
