using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop.Tests.ViewModels;

/// <summary>
/// Тесты модели представления поиска (Шаг 3 плана из
/// <c>docs/SEARCH_COMPLETENESS_AUDIT.md</c>).
///
/// Проверяются: счётчики «найдено/показано», доступность и поведение команды
/// «Показать ещё», отмена и обесценивание устаревших запросов, защита от
/// повторного запуска подгрузки, обработка ошибок, освобождение ресурсов и
/// корректность дерева после второй порции.
///
/// Служба поиска подставная, поэтому тесты проверяют логику модели
/// представления, а не SQLite; сам постраничный поиск покрыт тестами Шага 1.
/// </summary>
public sealed class FolderSearchViewModelTests
{
    private static readonly Guid RootSourceId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------------------------------------------------------------------
    // Счётчики, строка состояния и границы количеств
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task Search_ReportsCountersForMatchCount(
        int matchCount)
    {
        var matches = CreateMatches(matchCount);

        using var viewModel = CreateSearchingViewModel(
            matches,
            out _);

        await WaitUntilAsync(
            () => !viewModel.IsSearching);

        var expectedLoaded = Math.Min(matchCount, 200);

        Assert.Equal(
            matchCount,
            viewModel.TotalMatchCount);

        Assert.Equal(
            expectedLoaded,
            viewModel.LoadedMatchCount);

        Assert.Equal(
            matchCount > expectedLoaded,
            viewModel.HasMoreResults);

        Assert.Equal(
            expectedLoaded,
            Flatten(viewModel.Results).Count());

        var expectedSummary = matchCount == 0
            ? "Совпадений не найдено"
            : $"Найдено совпадений: {matchCount}; " +
              $"показано {expectedLoaded}";

        Assert.Equal(
            expectedSummary,
            viewModel.ResultsSummary);
    }

    [Fact]
    public async Task LoadMore_DeliversRemainingMatchesExactlyOnce()
    {
        using var viewModel = CreateSearchingViewModel(
            CreateMatches(201),
            out var pageService);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 200);

        Assert.True(viewModel.HasMoreResults);
        Assert.True(viewModel.LoadMoreCommand.CanExecute(null));

        viewModel.LoadMoreCommand.Execute(null);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 201);

        Assert.Equal(201, viewModel.TotalMatchCount);
        Assert.False(viewModel.HasMoreResults);
        Assert.False(viewModel.LoadMoreCommand.CanExecute(null));

        Assert.Equal(
            "Найдено совпадений: 201; показано 201",
            viewModel.ResultsSummary);

        var matchIds = Flatten(viewModel.Results)
            .Where(node => node.IsMatch)
            .Select(node => node.Id)
            .ToArray();

        Assert.Equal(201, matchIds.Length);

        Assert.Equal(
            matchIds.Length,
            matchIds.Distinct().Count());

        Assert.Equal(1, pageService.NextPageCallCount);
    }

    [Fact]
    public async Task LoadMore_WhenEverythingIsLoaded_DoesNothing()
    {
        using var viewModel = CreateSearchingViewModel(
            CreateMatches(200),
            out var pageService);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 200);

        Assert.False(viewModel.LoadMoreCommand.CanExecute(null));

        viewModel.LoadMoreCommand.Execute(null);

        await Task.Delay(50);

        Assert.Equal(0, pageService.NextPageCallCount);
        Assert.Equal(200, viewModel.LoadedMatchCount);
    }

    // ---------------------------------------------------------------------
    // Состав запроса и область источников
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Search_UsesPageSize200AndExplicitSourceScope()
    {
        var pageService = new StubFolderSearchPageService(
            CreateMatches(1));

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 1);

        var query = Assert.Single(pageService.FirstPageQueries);

        Assert.Equal(200, query.MaxResults);
        Assert.NotNull(query.RootSourceIds);
        Assert.Equal([RootSourceId], query.RootSourceIds);
        Assert.NotNull(query.VideoFileRootSourceIds);
        Assert.Empty(query.VideoFileRootSourceIds!);
    }

    [Fact]
    public void Search_WithoutSources_AsksToSelectSource()
    {
        using var viewModel = CreateViewModel(
            new StubFolderSearchPageService([]));

        viewModel.SearchText = "фестиваль";

        Assert.Equal(
            "Выберите хотя бы один источник для поиска",
            viewModel.ResultsSummary);

        Assert.Equal(0, viewModel.TotalMatchCount);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task Search_EmptyText_ShowsPromptAndClearsResults()
    {
        using var viewModel = CreateSearchingViewModel(
            CreateMatches(3),
            out _);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 3);

        viewModel.SearchText = "   ";

        Assert.Equal(
            "Введите запрос для поиска",
            viewModel.ResultsSummary);

        Assert.Equal(0, viewModel.TotalMatchCount);
        Assert.Equal(0, viewModel.LoadedMatchCount);
        Assert.False(viewModel.HasMoreResults);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task ChangingMode_RunsNewSearch()
    {
        using var viewModel = CreateSearchingViewModel(
            CreateMatches(2),
            out var pageService);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 2);

        viewModel.SearchMode = FolderSearchMode.Exact;

        await WaitUntilAsync(
            () => pageService.FirstPageCallCount == 2);

        Assert.Equal(
            FolderSearchMode.Exact,
            pageService.FirstPageQueries[1].Mode);
    }

    // ---------------------------------------------------------------------
    // Контекстные папки и дерево
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Counters_CountMatchesNotContextFolders()
    {
        var archive = CreateFolder(1, "Архив");
        var year = CreateFolder(2, "2025", parentFolderId: 1);
        var month = CreateFolder(
            3,
            "10_ОКТЯБРЬ",
            parentFolderId: 2);

        var contextProvider =
            new StubFolderSearchContextProvider(
                archive,
                year,
                month);

        using var viewModel = CreateViewModel(
            new StubFolderSearchPageService(
                [CreateFolder(10, "Фестиваль", parentFolderId: 3)]),
            contextProvider);

        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 1);

        // Контекстных папок три, но показано ровно одно совпадение.
        Assert.Equal(1, viewModel.TotalMatchCount);
        Assert.Equal(1, viewModel.LoadedMatchCount);
    }

    [Fact]
    public async Task LoadMore_RebuildsTreeWithAllAccumulatedMatches()
    {
        var archive = CreateFolder(1, "Архив");
        var year = CreateFolder(2, "2025", parentFolderId: 1);
        var month = CreateFolder(
            3,
            "10_ОКТЯБРЬ",
            parentFolderId: 2);

        var matches = new[]
        {
            CreateFolder(
                10,
                "Фестиваль недвижимости",
                parentFolderId: 3),
            CreateFolder(
                11,
                "Фестиваль памяти",
                parentFolderId: 3),
            CreateFolder(
                12,
                "Фестиваль кино",
                parentFolderId: 3)
        };

        var contextProvider =
            new StubFolderSearchContextProvider(
                archive,
                year,
                month);

        var pageService =
            new StubFolderSearchPageService(
                matches,
                pageSize: 2);

        using var viewModel = CreateViewModel(
            pageService,
            contextProvider);

        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 2);

        Assert.True(viewModel.HasMoreResults);

        viewModel.LoadMoreCommand.Execute(null);

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 3);

        // Дерево после второй порции: один корень «Архив» с цепочкой
        // «2025» → «10_ОКТЯБРЬ», под которой лежат все три совпадения.
        var root = Assert.Single(viewModel.Results);

        Assert.Equal(1, root.Id);

        var matchIds = Flatten(viewModel.Results)
            .Where(node => node.IsMatch)
            .Select(node => node.Id)
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(
            new long[] { 10, 11, 12 },
            matchIds);

        // Контекст запрашивался для всех накопленных совпадений, а не только
        // для последней порции.
        Assert.Equal(
            new long[] { 10, 11, 12 },
            contextProvider.LastMatchIds
                .OrderBy(id => id)
                .ToArray());
    }

    [Fact]
    public async Task LoadMore_KeepsEarlierMatchesVisible()
    {
        var matches = CreateMatches(5);

        var pageService =
            new StubFolderSearchPageService(
                matches,
                pageSize: 2);

        using var viewModel = CreateViewModel(pageService);

        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 2);

        var firstPageIds = Flatten(viewModel.Results)
            .Where(node => node.IsMatch)
            .Select(node => node.Id)
            .ToArray();

        while (viewModel.HasMoreResults)
        {
            var loaded = viewModel.LoadedMatchCount;

            viewModel.LoadMoreCommand.Execute(null);

            await WaitUntilAsync(
                () => viewModel.LoadedMatchCount > loaded);
        }

        var allIds = Flatten(viewModel.Results)
            .Where(node => node.IsMatch)
            .Select(node => node.Id)
            .ToArray();

        Assert.Equal(5, allIds.Length);

        Assert.All(
            firstPageIds,
            id => Assert.Contains(id, allIds));
    }

    // ---------------------------------------------------------------------
    // Отмена, устаревшие ответы и повторный вход
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ChangingQuery_CancelsInFlightFirstPage()
    {
        var staleGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pageService = new StubFolderSearchPageService(
            query => query.Text == "первый"
                ? [CreateFolder(10, "Первый")]
                : [CreateFolder(20, "Второй")]);

        // Отмена наблюдается: при смене запроса первый вызов обязан прерваться.
        pageService.BeforeFirstPageAsync =
            token => staleGate.Task.WaitAsync(token);

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "первый";

        await WaitUntilAsync(
            () => pageService.FirstPageCallCount == 1);

        viewModel.SearchText = "второй";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 1);

        Assert.Equal(
            "Найдено совпадений: 1; показано 1",
            viewModel.ResultsSummary);

        Assert.Equal(
            20,
            Flatten(viewModel.Results)
                .Single(node => node.IsMatch)
                .Id);
    }

    [Fact]
    public async Task ChangingQuery_DiscardsStaleFirstPageResult()
    {
        var staleGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pageService = new StubFolderSearchPageService(
            query => query.Text == "старый"
                ? [CreateFolder(10, "Старый запрос")]
                : [CreateFolder(20, "Новый запрос")]);

        // Отмена здесь не наблюдается намеренно: устаревший ответ доходит до
        // конца и обязан быть отброшен проверкой версии перед публикацией.
        pageService.BeforeFirstPageAsync =
            _ => staleGate.Task;

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "старый";

        await WaitUntilAsync(
            () => pageService.FirstPageCallCount == 1);

        viewModel.SearchText = "новый";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 1);

        staleGate.SetResult();

        await Task.Delay(200);

        Assert.Equal(1, viewModel.LoadedMatchCount);
        Assert.Equal(1, viewModel.TotalMatchCount);

        var matchNode = Flatten(viewModel.Results)
            .Single(node => node.IsMatch);

        Assert.Equal(20, matchNode.Id);
    }

    [Fact]
    public async Task LoadMore_IgnoresRepeatedInvocation()
    {
        var nextPageGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pageService = new StubFolderSearchPageService(
            CreateMatches(201));

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 200);

        pageService.BeforeNextPageAsync =
            _ => nextPageGate.Task;

        viewModel.LoadMoreCommand.Execute(null);

        await WaitUntilAsync(
            () => pageService.NextPageCallCount == 1);

        Assert.True(viewModel.IsLoadingMore);
        Assert.False(viewModel.LoadMoreCommand.CanExecute(null));

        viewModel.LoadMoreCommand.Execute(null);

        await Task.Delay(50);

        Assert.Equal(1, pageService.NextPageCallCount);

        nextPageGate.SetResult();

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 201);

        Assert.False(viewModel.IsLoadingMore);
    }

    [Fact]
    public async Task LoadMore_AfterNewSearch_DoesNotMixSessions()
    {
        var pageService = new StubFolderSearchPageService(
            query => query.Text == "первый"
                ? CreateMatches(201, idOffset: 1000)
                : CreateMatches(3, idOffset: 5000),
            pageSize: 0);

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "первый";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 200);

        viewModel.SearchText = "второй";

        await WaitUntilAsync(
            () => viewModel.TotalMatchCount == 3 &&
                  !viewModel.IsSearching);

        Assert.Equal(3, viewModel.LoadedMatchCount);
        Assert.False(viewModel.HasMoreResults);

        var matchIds = Flatten(viewModel.Results)
            .Where(node => node.IsMatch)
            .Select(node => node.Id)
            .ToArray();

        Assert.Equal(3, matchIds.Length);

        Assert.All(
            matchIds,
            id => Assert.InRange(id, 5000, 5999));
    }

    // ---------------------------------------------------------------------
    // Ошибки и освобождение ресурсов
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Search_ServiceThrows_ReportsError()
    {
        var pageService = new StubFolderSearchPageService(
            _ => throw new InvalidOperationException("boom"));

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.ResultsSummary ==
                  "Не удалось выполнить поиск");

        Assert.Equal(0, viewModel.TotalMatchCount);
        Assert.Equal(0, viewModel.LoadedMatchCount);
        Assert.False(viewModel.HasMoreResults);
        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.IsSearching);
    }

    [Fact]
    public async Task LoadMore_ServiceThrows_KeepsShownResults()
    {
        var pageService = new StubFolderSearchPageService(
            CreateMatches(201));

        using var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => viewModel.LoadedMatchCount == 200);

        pageService.BeforeNextPageAsync =
            _ => throw new InvalidOperationException("boom");

        viewModel.LoadMoreCommand.Execute(null);

        await WaitUntilAsync(
            () => !viewModel.IsLoadingMore);

        Assert.Equal(201, viewModel.TotalMatchCount);
        Assert.Equal(200, viewModel.LoadedMatchCount);
        Assert.True(viewModel.HasMoreResults);

        Assert.Equal(
            200,
            Flatten(viewModel.Results)
                .Count(node => node.IsMatch));
    }

    [Fact]
    public async Task Dispose_DuringSearch_DoesNotPublishResults()
    {
        var firstPageGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pageService = new StubFolderSearchPageService(
            [CreateFolder(10, "Фестиваль")]);

        pageService.BeforeFirstPageAsync =
            _ => firstPageGate.Task;

        var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);

        viewModel.SearchText = "фестиваль";

        await WaitUntilAsync(
            () => pageService.FirstPageCallCount == 1);

        viewModel.Dispose();

        firstPageGate.SetResult();

        await Task.Delay(100);

        Assert.Empty(viewModel.Results);
        Assert.Equal(0, viewModel.LoadedMatchCount);
        Assert.Equal(0, viewModel.TotalMatchCount);
    }

    // ---------------------------------------------------------------------
    // Вспомогательные методы
    // ---------------------------------------------------------------------

    private static FolderSearchViewModel CreateViewModel(
        IFolderSearchPageService pageService,
        IFolderSearchContextProvider? contextProvider = null)
    {
        return new FolderSearchViewModel(
            pageService,
            contextProvider ??
                new StubFolderSearchContextProvider(),
            CreateTreeBuilder(),
            NullLogger<FolderSearchViewModel>.Instance,
            debounceMilliseconds: 0);
    }

    private static FolderSearchViewModel CreateSearchingViewModel(
        IReadOnlyList<FolderSearchResult> matches,
        out StubFolderSearchPageService pageService)
    {
        pageService =
            new StubFolderSearchPageService(matches);

        var viewModel = CreateViewModel(pageService);
        viewModel.SetRootSourceIds([RootSourceId]);
        viewModel.SearchText = "фестиваль";

        return viewModel;
    }

    private static FolderSearchTreeBuilder CreateTreeBuilder()
    {
        return new FolderSearchTreeBuilder(
            new FolderNameHighlightService(
                new TextNormalizationService(),
                new RussianSearchStemService()));
    }

    private static FolderSearchResult CreateFolder(
        long id,
        string name,
        long? parentFolderId = null)
    {
        return new FolderSearchResult(
            Id: id,
            FullPath: $@"\\archive\{name}",
            Name: name,
            NormalizedName: name.ToLowerInvariant(),
            ParentFolderId: parentFolderId,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static IReadOnlyList<FolderSearchResult>
        CreateMatches(
            int count,
            int idOffset = 0)
    {
        return Enumerable
            .Range(1, count)
            .Select(index => CreateFolder(
                idOffset + index,
                $"Фестиваль {index:D4}"))
            .ToArray();
    }

    private static IEnumerable<FolderSearchTreeNode> Flatten(
        IEnumerable<FolderSearchTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition)
    {
        var deadline = DateTime.UtcNow +
                       TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    "Условие не выполнено за отведённое время.");
            }

            await Task.Delay(5);
        }
    }
}
