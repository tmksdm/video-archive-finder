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

    public async Task<bool> RemoveAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            return false;
        }

        await _accessLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var sources = (await _store.LoadAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();

            var removedCount = sources.RemoveAll(
                source => source.Id == sourceId);

            if (removedCount == 0)
            {
                return false;
            }

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
