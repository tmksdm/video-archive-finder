using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class SqliteFolderSearchContextProviderTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetContextFoldersAsync_ReturnsAncestorsWithoutSibling()
    {
        var context = await CreateContextAsync();

        var rootPath = Path.Combine(
            _temporaryDirectory,
            "Архив");

        var parentPath = Path.Combine(
            rootPath,
            "Транспорт");

        var matchPath = Path.Combine(
            parentPath,
            "Железная дорога");

        var siblingPath = Path.Combine(
            parentPath,
            "Вторая речка");

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(
                context,
                rootPath,
                "Архив"),

            CreateFolder(
                context,
                parentPath,
                "Транспорт",
                rootPath),

            CreateFolder(
                context,
                matchPath,
                "Железная дорога",
                parentPath),

            CreateFolder(
                context,
                siblingPath,
                "Вторая речка",
                parentPath)
        ]);

        var indexedFolders =
            await context.Repository
                .GetByRootSourceIdAsync(
                    context.RootSourceId);

        var match = indexedFolders.Single(
            folder =>
                folder.Name == "Железная дорога");

        var contextFolders =
            await context.Provider.GetContextFoldersAsync(
            [
                ToSearchResult(match)
            ]);

        var names = contextFolders
            .Select(folder => folder.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Архив", names);
        Assert.Contains("Транспорт", names);
        Assert.Contains("Железная дорога", names);

        Assert.DoesNotContain(
            "Вторая речка",
            names);
    }

    [Fact]
    public async Task GetContextFoldersAsync_ReturnsSharedParentOnce()
    {
        var context = await CreateContextAsync();

        var rootPath = Path.Combine(
            _temporaryDirectory,
            "Архив");

        var firstMatchPath = Path.Combine(
            rootPath,
            "Автомобильная дорога");

        var secondMatchPath = Path.Combine(
            rootPath,
            "Железная дорога");

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(
                context,
                rootPath,
                "Архив"),

            CreateFolder(
                context,
                firstMatchPath,
                "Автомобильная дорога",
                rootPath),

            CreateFolder(
                context,
                secondMatchPath,
                "Железная дорога",
                rootPath)
        ]);

        var indexedFolders =
            await context.Repository
                .GetByRootSourceIdAsync(
                    context.RootSourceId);

        var matches = indexedFolders
            .Where(folder =>
                folder.Name.Contains(
                    "дорога",
                    StringComparison.OrdinalIgnoreCase))
            .Select(ToSearchResult)
            .ToArray();

        var contextFolders =
            await context.Provider.GetContextFoldersAsync(
                matches);

        Assert.Equal(3, contextFolders.Count);

        Assert.Single(
            contextFolders,
            folder => folder.Name == "Архив");
    }

    private async Task<SearchContext> CreateContextAsync()
    {
        var directoryProvider =
            new TestApplicationDataDirectoryProvider(
                _temporaryDirectory);

        var databasePathProvider =
            new IndexDatabasePathProvider(
                directoryProvider);

        var initializer =
            new SqliteIndexDatabaseInitializer(
                databasePathProvider,
                NullLogger<
                    SqliteIndexDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        var repository =
            new SqliteFolderIndexRepository(
                databasePathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        var provider =
            new SqliteFolderSearchContextProvider(
                databasePathProvider,
                NullLogger<
                    SqliteFolderSearchContextProvider>.Instance);

        return new SearchContext(
            repository,
            provider,
            new TextNormalizationService(),
            new RussianSearchStemService(),
            Guid.NewGuid());
    }

    private static FolderIndexUpsertItem CreateFolder(
        SearchContext context,
        string fullPath,
        string name,
        string? parentFullPath = null)
    {
        var tokens =
            context.NormalizationService.Tokenize(name);

        return new FolderIndexUpsertItem(
            FullPath: fullPath,
            Name: name,
            NormalizedName:
                context.NormalizationService.Normalize(name),
            SearchTokens:
                string.Join(' ', tokens),
            SearchStems:
                context.StemService.CreateStemText(tokens),
            ParentFullPath: parentFullPath,
            RootSourceId: context.RootSourceId,
            IsAvailable: true,
            LastSeenUtc: DateTimeOffset.UtcNow,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static FolderSearchResult ToSearchResult(
        IndexedFolder folder)
    {
        return new FolderSearchResult(
            Id: folder.Id,
            FullPath: folder.FullPath,
            Name: folder.Name,
            NormalizedName: folder.NormalizedName,
            ParentFolderId: folder.ParentFolderId,
            RootSourceId: folder.RootSourceId,
            IsAvailable: folder.IsAvailable,
            DirectSubfolderCount:
                folder.DirectSubfolderCount,
            DirectVideoFileCount:
                folder.DirectVideoFileCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private sealed record SearchContext(
        SqliteFolderIndexRepository Repository,
        SqliteFolderSearchContextProvider Provider,
        ITextNormalizationService NormalizationService,
        ISearchStemService StemService,
        Guid RootSourceId);

    private sealed class TestApplicationDataDirectoryProvider
        : IApplicationDataDirectoryProvider
    {
        private readonly string _directoryPath;

        public TestApplicationDataDirectoryProvider(
            string directoryPath)
        {
            _directoryPath = directoryPath;
        }

        public string GetApplicationDataDirectory()
        {
            return _directoryPath;
        }
    }
}
