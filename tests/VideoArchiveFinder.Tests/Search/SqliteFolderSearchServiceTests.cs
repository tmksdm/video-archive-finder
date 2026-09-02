using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class SqliteFolderSearchServiceTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(
        "почта",
        "Почта",
        "Почты",
        "Почтовый",
        "Почтовая",
        "Почтальон",
        "Почтамт")]
    [InlineData(
        "почт",
        "Почта",
        "Почты",
        "Почтовый",
        "Почтовая",
        "Почтальон",
        "Почтамт")]
    [InlineData(
        "дорога",
        "Дороги",
        "Дорожный",
        "Придорожный")]
    [InlineData(
        "дорог",
        "Дороги",
        "Дорожный",
        "Придорожный")]
    [InlineData(
        "поезд",
        "Поезда",
        "Поездной")]
    [InlineData(
        "машина",
        "Машины",
        "Машинный")]
    [InlineData(
        "море",
        "Морской")]
    public async Task SearchAsync_SmartMode_FindsRequiredForms(
        string queryText,
        params string[] expectedNames)
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Почта",
            "Почты",
            "Почтовый",
            "Почтовая",
            "Почтальон",
            "Почтамт",
            "Дороги",
            "Дорожный",
            "Придорожный",
            "Поезда",
            "Поездной",
            "Машины",
            "Машинный",
            "Морской",
            "Совершенно другое");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    queryText,
                    FolderSearchMode.Smart));

        var resultNames =
            results
                .Select(result => result.Name)
                .ToHashSet(StringComparer.Ordinal);

        foreach (var expectedName in expectedNames)
        {
            Assert.Contains(
                expectedName,
                resultNames);
        }

        Assert.DoesNotContain(
            "Совершенно другое",
            resultNames);
    }

    [Fact]
    public async Task SearchAsync_ExactMode_DoesNotExpandWordForms()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Почта",
            "Почты",
            "Почтовый",
            "Почтальон",
            "Почтамт");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "почта",
                    FolderSearchMode.Exact));

        var names =
            results
                .Select(result => result.Name)
                .ToArray();

        Assert.Contains("Почта", names);
        Assert.Contains("Почтальон", names);
        Assert.Contains("Почтамт", names);

        Assert.DoesNotContain("Почты", names);
        Assert.DoesNotContain("Почтовый", names);
    }

    [Fact]
    public async Task SearchAsync_NormalizesCaseAndYo()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Съёмка Ёлки",
            "Елки на площади",
            "Другая папка");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "ЁЛК",
                    FolderSearchMode.Smart));

        var names =
            results
                .Select(result => result.Name)
                .ToArray();

        Assert.Contains("Съёмка Ёлки", names);
        Assert.Contains("Елки на площади", names);
        Assert.DoesNotContain("Другая папка", names);
    }

    [Fact]
    public async Task SearchAsync_UsesSeparatorsAndAllQueryWords()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "ЖДвокзал_Железная-дорога",
            "Только железная",
            "Только дорога");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "железная дорога",
                    FolderSearchMode.Smart));

        var result = Assert.Single(results);

        Assert.Equal(
            "ЖДвокзал_Железная-дорога",
            result.Name);
    }

    [Fact]
    public async Task SearchAsync_DoesNotMatchChildByParentNameOrFullPath()
    {
        var context = await CreateContextAsync();

        var parentPath =
            Path.Combine(
                _temporaryDirectory,
                "Железная дорога");

        var childPath =
            Path.Combine(
                parentPath,
                "Вторая речка");

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(
                context,
                parentPath,
                "Железная дорога"),

            CreateFolder(
                context,
                childPath,
                "Вторая речка",
                parentPath)
        ]);

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "дорог",
                    FolderSearchMode.Smart));

        Assert.Contains(
            results,
            result => result.Name ==
                "Железная дорога");

        Assert.DoesNotContain(
            results,
            result => result.Name ==
                "Вторая речка");
    }

    [Fact]
    public async Task SearchAsync_TreatsSqlPatternCharactersLiterally()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Архив 100%_готов",
            "Архив 100 процентов",
            "Обычная папка");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "%_",
                    FolderSearchMode.Exact));

        var result = Assert.Single(results);

        Assert.Equal(
            "Архив 100%_готов",
            result.Name);
    }

    [Fact]
    public async Task SearchAsync_RespectsMaximumResultCount()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Почта 01",
            "Почта 02",
            "Почта 03",
            "Почта 04");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "почта",
                    FolderSearchMode.Smart,
                    MaxResults: 2));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_RootSourceIds_ExcludesOtherSources()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Нужная папка");

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(
                context,
                Path.Combine(
                    _temporaryDirectory,
                    "Orphaned"),
                "Нужная осиротевшая папка") with
            {
                RootSourceId = Guid.NewGuid()
            }
        ]);

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "нужная",
                    FolderSearchMode.Smart,
                    RootSourceIds: [context.RootSourceId]));

        var result = Assert.Single(results);
        Assert.Equal("Нужная папка", result.Name);
    }

    [Fact]
    public async Task SearchAsync_EmptyRootSourceIds_ReturnsEmpty()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Нужная папка");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "нужная",
                    FolderSearchMode.Smart,
                    RootSourceIds: []));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmptyResult()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            "Почта");

        var results =
            await context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "   ",
                    FolderSearchMode.Smart));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_PreCancelledToken_ThrowsCancellation()
    {
        var context = await CreateContextAsync();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "почта",
                    FolderSearchMode.Smart),
                cancellationTokenSource.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task SearchAsync_InvalidMaximumResultCount_Throws(
        int maxResults)
    {
        var context = await CreateContextAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "почта",
                    FolderSearchMode.Smart,
                    maxResults)));
    }

    private async Task<SearchTestContext>
        CreateContextAsync()
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

        var normalizationService =
            new TextNormalizationService();

        var stemService =
            new RussianSearchStemService();

        var searchService =
            new SqliteFolderSearchService(
                databasePathProvider,
                normalizationService,
                stemService,
                NullLogger<
                    SqliteFolderSearchService>.Instance);

        return new SearchTestContext(
            repository,
            searchService,
            normalizationService,
            stemService,
            Guid.NewGuid());
    }

    private async Task AddFoldersAsync(
        SearchTestContext context,
        params string[] names)
    {
        var folders =
            names
                .Select(
                    (name, index) =>
                        CreateFolder(
                            context,
                            Path.Combine(
                                _temporaryDirectory,
                                $"Folder-{index:D3}-{name}"),
                            name))
                .ToArray();

        await context.Repository.UpsertBatchAsync(
            folders);
    }

    private static FolderIndexUpsertItem CreateFolder(
        SearchTestContext context,
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

    private sealed record SearchTestContext(
        SqliteFolderIndexRepository Repository,
        SqliteFolderSearchService SearchService,
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
