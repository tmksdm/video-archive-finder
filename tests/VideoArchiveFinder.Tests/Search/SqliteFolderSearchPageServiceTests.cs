using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.Tests.Search;

/// <summary>
/// Тесты постраничного поиска (Шаги 1 и 6 плана из
/// <c>docs/SEARCH_COMPLETENESS_AUDIT.md</c>).
///
/// Проверяется: полнота выдачи при любом числе совпадений, отсутствие пропусков
/// и дубликатов, точные счётчики, приоритет папок в бюджете порции, сохранение
/// эталонного порядка сортировки и эквивалентность постраничной выборки
/// независимому эталону <c>OFFSET</c>/<c>LIMIT</c> на глубоких страницах.
/// Дополнительно закреплены CHANGE-STOP по <c>data_version</c>, владение
/// сессионным соединением, <c>ValidateSessionAsync</c>/<c>ReleaseSession</c> и
/// офлайн-поиск по локальному индексу при недоступном UNC-пути источника.
/// </summary>
public sealed class SqliteFolderSearchPageServiceTests
    : IDisposable
{
    private const int DefaultPageSize = 200;

    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    private readonly List<SqliteFolderSearchPageService>
        _pageServices = [];

    // ---------------------------------------------------------------------
    // Полнота выдачи и счётчики
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchFirstPageAsync_WhitespaceQuery_ReturnsEmptyPage()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 5);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "   ", videoSource: true));

        Assert.Empty(page.Matches);
        Assert.Equal(0, page.TotalMatchCount);
        Assert.False(page.HasMoreResults);
    }

    [Fact]
    public async Task SearchFirstPageAsync_NoMatches_ReturnsEmptyPage()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 5);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "несуществующий запрос", videoSource: true));

        Assert.Empty(page.Matches);
        Assert.Equal(0, page.TotalMatchCount);
        Assert.False(page.HasMoreResults);
    }

    [Fact]
    public async Task SearchFirstPageAsync_CountsEachStreamAndTotalExactly()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 7);
        await AddVideoFilesAsync(context, videoCount: 13);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(
                context,
                "фестиваль сборка",
                folderSource: true,
                videoSource: true));

        Assert.Equal(7, page.FolderMatchCount);
        Assert.Equal(13, page.VideoMatchCount);
        Assert.Equal(20, page.TotalMatchCount);

        // Счётчики точные и полные: порция укладывается целиком, потому что
        // совпадений меньше её размера.
        Assert.Equal(20, page.Matches.Count);
        Assert.Equal(20, page.LoadedMatchCount);
        Assert.False(page.HasMoreResults);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(1000)]
    [InlineData(10_000)]
    public async Task Pages_DeliverAllMatchesExactly(int matchCount)
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: matchCount);

        var query = CreateQuery(context, "фестиваль", videoSource: true);

        var page = await context.PageService.SearchFirstPageAsync(query);

        var collected = new List<FolderSearchResult>(page.Matches);

        Assert.Equal(matchCount, page.TotalMatchCount);
        Assert.Equal(
            Math.Min(matchCount, DefaultPageSize),
            page.Matches.Count);

        while (page.HasMoreResults)
        {
            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);

            collected.AddRange(page.Matches);
        }

        Assert.Equal(matchCount, collected.Count);
        Assert.Equal(matchCount, page.LoadedMatchCount);

        var expected = await ReadReferenceMatchesAsync(context, query);

        Assert.Equal(
            expected.Select(result => result.Id).ToArray(),
            collected.Select(result => result.Id).ToArray());
    }

    [Fact]
    public async Task Pages_DoNotRepeatOrSkipMatchesWithUnchangedIndex()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 1_234);

        var query = CreateQuery(context, "фестиваль", videoSource: true);

        var page = await context.PageService.SearchFirstPageAsync(query);

        var collected = new List<FolderSearchResult>(page.Matches);
        var pageSizes = new List<int> { page.Matches.Count };

        while (page.HasMoreResults)
        {
            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);

            collected.AddRange(page.Matches);
            pageSizes.Add(page.Matches.Count);
        }

        // Ни одна порция не превышает запрошенный размер.
        Assert.All(
            pageSizes,
            size => Assert.InRange(size, 1, DefaultPageSize));

        // Ни одного повтора и ни одного пропуска.
        Assert.Equal(
            collected.Count,
            collected.Select(result => result.Id).Distinct().Count());

        Assert.Equal(1_234, collected.Count);

        var expected = await ReadReferenceMatchesAsync(context, query);

        Assert.Equal(
            expected.Select(result => result.Id).ToArray(),
            collected.Select(result => result.Id).ToArray());
    }

    [Fact]
    public async Task SearchNextPageAsync_WithoutRemainingMatches_ReturnsEmptyPage()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 3);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        Assert.Equal(3, page.Matches.Count);
        Assert.False(page.HasMoreResults);

        var next = await context.PageService.SearchNextPageAsync(
            page.NextSession);

        Assert.Empty(next.Matches);
        Assert.Equal(3, next.TotalMatchCount);
        Assert.Equal(3, next.LoadedMatchCount);
        Assert.False(next.HasMoreResults);
    }

    [Fact]
    public async Task SearchFirstPageAsync_MatchesLegacySearchResults()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 30);
        await AddVideoFilesAsync(context, videoCount: 900);

        var query = CreateQuery(
            context,
            "фестиваль сборка",
            folderSource: true,
            videoSource: true);

        // Первая порция постраничного поиска обязана совпасть с выдачей
        // существующего сервиса: тот же состав, тот же порядок, тот же бюджет.
        var legacy = await context.SearchService.SearchAsync(query);

        var page = await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(
            legacy.Select(result => result.Id).ToArray(),
            page.Matches.Select(result => result.Id).ToArray());

        Assert.Equal(
            legacy.Select(result => result.Name).ToArray(),
            page.Matches.Select(result => result.Name).ToArray());
    }

    // ---------------------------------------------------------------------
    // Совместимость прежнего поискового API (Шаг 2)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("фестиваль сборка", FolderSearchMode.Smart, true, true)]
    [InlineData("фестиваль", FolderSearchMode.Smart, false, true)]
    [InlineData("почта фестиваль", FolderSearchMode.Smart, true, false)]
    [InlineData("фестиваль сборка", FolderSearchMode.Exact, true, true)]
    [InlineData("почта", FolderSearchMode.Exact, true, false)]
    public async Task SearchAsync_ReturnsExactlyTheFirstPageOfPagedSearch(
        string queryText,
        FolderSearchMode mode,
        bool folderSource,
        bool videoSource)
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 30);
        await AddVideoFilesAsync(context, videoCount: 900);

        var query = CreateQuery(
            context,
            queryText,
            folderSource,
            videoSource,
            mode: mode);

        // Прежний API обязан вернуть ровно первую порцию постраничного поиска:
        // тот же состав, тот же порядок и то же ограничение количества.
        var legacy = await context.SearchService.SearchAsync(query);

        var page = await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(
            page.Matches.Select(match => match.Id).ToArray(),
            legacy.Select(match => match.Id).ToArray());

        Assert.Equal(
            page.Matches.Select(match => match.Name).ToArray(),
            legacy.Select(match => match.Name).ToArray());

        Assert.Equal(
            page.Matches.Select(match => match.IsVideoFile).ToArray(),
            legacy.Select(match => match.IsVideoFile).ToArray());
    }

    [Fact]
    public async Task SearchAsync_WithoutFolderSourceScope_SearchesAllFolders()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 40);

        // null в области источников означает «без ограничения»: прежний поиск
        // искал по всем папкам индекса, и это поведение сохранено.
        var query = new FolderSearchQuery(
            "почта фестиваль",
            FolderSearchMode.Smart,
            DefaultPageSize,
            RootSourceIds: null,
            VideoFileRootSourceIds: []);

        var legacy = await context.SearchService.SearchAsync(query);

        Assert.Equal(40, legacy.Count);

        var page = await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(40, page.FolderMatchCount);
        Assert.Equal(
            page.Matches.Select(match => match.Id).ToArray(),
            legacy.Select(match => match.Id).ToArray());
    }

    [Fact]
    public async Task SearchAsync_WithEmptyFolderSourceScope_ReturnsNothing()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 40);

        // Пустая коллекция, в отличие от null, означает отсутствие области поиска.
        var query = new FolderSearchQuery(
            "почта фестиваль",
            FolderSearchMode.Smart,
            DefaultPageSize,
            RootSourceIds: [],
            VideoFileRootSourceIds: []);

        Assert.Empty(await context.SearchService.SearchAsync(query));

        var page = await context.PageService.SearchFirstPageAsync(query);

        Assert.Empty(page.Matches);
        Assert.Equal(0, page.TotalMatchCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task SearchAsync_AcceptsMaximumResultCountUpTo1000(
        int maxResults)
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 250);

        var results = await context.SearchService.SearchAsync(
            CreateQuery(
                context,
                "почта фестиваль",
                folderSource: true,
                pageSize: maxResults));

        Assert.Equal(Math.Min(maxResults, 250), results.Count);
    }

    [Fact]
    public void ServiceRegistration_WiresPagedSearchWithoutCycles()
    {
        var services = new ServiceCollection();

        services.AddVideoArchiveFinderInfrastructure();

        var searchDescriptor = Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IFolderSearchService));

        Assert.Equal(
            typeof(SqliteFolderSearchService),
            searchDescriptor.ImplementationType);

        var pageDescriptor = Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IFolderSearchPageService));

        Assert.Equal(
            typeof(SqliteFolderSearchPageService),
            pageDescriptor.ImplementationType);

        // Направление зависимости: точечный поиск зависит от постраничного, а
        // обратной зависимости нет, поэтому цикла в графе быть не может.
        var searchParameters = typeof(SqliteFolderSearchService)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.Contains(
            searchParameters,
            parameter =>
                parameter.ParameterType ==
                    typeof(IFolderSearchPageService));

        var pageParameters = typeof(SqliteFolderSearchPageService)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.DoesNotContain(
            pageParameters,
            parameter =>
                parameter.ParameterType ==
                    typeof(IFolderSearchService));

        Assert.DoesNotContain(
            pageParameters,
            parameter =>
                parameter.ParameterType ==
                    typeof(IFolderSearchPageService));
    }

    [Fact]
    public async Task SearchAsync_WhenIndexDatabaseIsMissing_ThrowsWithoutCreatingIt()
    {
        var context = await CreateContextAsync(initializeDatabase: false);

        await Assert.ThrowsAnyAsync<SqliteException>(
            () => context.SearchService.SearchAsync(
                CreateQuery(context, "почта", folderSource: true)));

        // Поиск открывает индекс только для чтения и не создаёт его: прежде
        // неудачный поиск оставлял после себя пустой файл базы.
        Assert.False(File.Exists(context.DatabasePath));
    }

    // ---------------------------------------------------------------------
    // Эквивалентность эталонной выборке OFFSET/LIMIT
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Pages_MatchReferenceOffsetOrderPageByPage()
    {
        var context = await CreateContextAsync();

        // Достаточно совпадений, чтобы страницы были глубокими и непустыми:
        // 40 папок занимают начало первой порции, дальше идут только файлы.
        await AddFoldersAsync(context, count: 40);
        await AddVideoFilesAsync(context, videoCount: 5_000);

        var query = CreateQuery(
            context,
            "фестиваль сборка",
            folderSource: true,
            videoSource: true);

        var reference = await ReadReferenceMatchesAsync(context, query);

        Assert.Equal(5_040, reference.Count);

        var page = await context.PageService.SearchFirstPageAsync(query);

        var position = 0;
        var pageIndex = 0;

        while (true)
        {
            var expectedSlice = reference
                .Skip(position)
                .Take(DefaultPageSize)
                .ToArray();

            Assert.Equal(
                expectedSlice.Select(result => result.Id).ToArray(),
                page.Matches.Select(result => result.Id).ToArray());

            // Каждая сравниваемая страница непуста, включая последнюю с данными.
            Assert.NotEmpty(page.Matches);

            position += page.Matches.Count;
            pageIndex++;

            if (!page.HasMoreResults)
            {
                break;
            }

            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);
        }

        Assert.Equal(reference.Count, position);
        Assert.Equal(26, pageIndex);
    }

    [Fact]
    public async Task Pages_SplitEqualSortKeysAcrossPagesWithoutDuplicates()
    {
        var context = await CreateContextAsync();

        // Одинаковые имена в разных папках дают одинаковый ключ сортировки:
        // порядок задаёт только Id, и именно на таких строках ошибка курсора
        // приводит к дубликатам или пропускам.
        await AddIdenticalNamedVideoFilesAsync(context, count: 5);

        var query = CreateQuery(
            context,
            "фестиваль",
            videoSource: true,
            pageSize: 2);

        var page = await context.PageService.SearchFirstPageAsync(query);

        var collected = new List<FolderSearchResult>(page.Matches);

        Assert.Equal(2, page.Matches.Count);
        Assert.True(page.HasMoreResults);

        while (page.HasMoreResults)
        {
            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);

            collected.AddRange(page.Matches);
        }

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Select(result => result.Id).Distinct().Count());

        // Ключ сортировки у всех строк одинаковый, поэтому порядок задаёт
        // возрастающий идентификатор строки; в результате видеофайла он
        // отрицателен, отсюда проверка через знак.
        var rowKeys = collected.Select(result => -result.Id).ToArray();

        Assert.Equal(rowKeys.OrderBy(key => key).ToArray(), rowKeys);

        var expected = await ReadReferenceMatchesAsync(context, query);

        Assert.Equal(
            expected.Select(result => result.Id).ToArray(),
            collected.Select(result => result.Id).ToArray());
    }

    [Fact]
    public async Task Pages_MatchReferenceOrderInExactMode()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 7);
        await AddVideoFilesAsync(context, videoCount: 600);

        var query = CreateQuery(
            context,
            "фестиваль сборка",
            folderSource: true,
            videoSource: true,
            mode: FolderSearchMode.Exact);

        var reference = await ReadReferenceMatchesAsync(context, query);

        var page = await context.PageService.SearchFirstPageAsync(query);

        var collected = new List<FolderSearchResult>(page.Matches);

        while (page.HasMoreResults)
        {
            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);

            collected.AddRange(page.Matches);
        }

        Assert.Equal(reference.Count, collected.Count);
        Assert.Equal(
            reference.Select(result => result.Id).ToArray(),
            collected.Select(result => result.Id).ToArray());
    }

    // ---------------------------------------------------------------------
    // Приоритет папок и бюджет порции
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Pages_PutFoldersFirstAndSpendRemainingBudgetOnVideoFiles()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(context, count: 30);
        await AddVideoFilesAsync(context, videoCount: 250);

        var query = CreateQuery(
            context,
            "фестиваль сборка",
            folderSource: true,
            videoSource: true);

        var firstPage = await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(30, firstPage.FolderMatchCount);
        Assert.Equal(250, firstPage.VideoMatchCount);
        Assert.Equal(280, firstPage.TotalMatchCount);

        Assert.Equal(DefaultPageSize, firstPage.Matches.Count);

        Assert.All(
            firstPage.Matches.Take(30),
            match => Assert.False(match.IsVideoFile));

        Assert.All(
            firstPage.Matches.Skip(30),
            match => Assert.True(match.IsVideoFile));

        Assert.Equal(30, firstPage.FolderLoadedCount);
        Assert.Equal(170, firstPage.VideoLoadedCount);

        var secondPage = await context.PageService.SearchNextPageAsync(
            firstPage.NextSession);

        Assert.Equal(80, secondPage.Matches.Count);

        Assert.All(
            secondPage.Matches,
            match => Assert.True(match.IsVideoFile));

        Assert.Equal(30, secondPage.FolderLoadedCount);
        Assert.Equal(250, secondPage.VideoLoadedCount);
        Assert.False(secondPage.HasMoreResults);
    }

    [Fact]
    public async Task Pages_TransferRemainingFolderBudgetToVideoFiles()
    {
        var context = await CreateContextAsync();

        // Совпадений по папкам больше, чем помещается в одну порцию: остаток
        // бюджета папок обязан перейти файлам на следующей порции.
        await AddFoldersAsync(context, count: 250);
        await AddVideoFilesAsync(context, videoCount: 500);

        var query = CreateQuery(
            context,
            "фестиваль сборка",
            folderSource: true,
            videoSource: true);

        var firstPage = await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(DefaultPageSize, firstPage.Matches.Count);
        Assert.All(
            firstPage.Matches,
            match => Assert.False(match.IsVideoFile));

        Assert.Equal(200, firstPage.FolderLoadedCount);
        Assert.Equal(0, firstPage.VideoLoadedCount);

        var secondPage = await context.PageService.SearchNextPageAsync(
            firstPage.NextSession);

        Assert.Equal(DefaultPageSize, secondPage.Matches.Count);

        Assert.Equal(
            50,
            secondPage.Matches.Count(match => !match.IsVideoFile));

        Assert.Equal(
            150,
            secondPage.Matches.Count(match => match.IsVideoFile));

        Assert.Equal(250, secondPage.FolderLoadedCount);
        Assert.Equal(150, secondPage.VideoLoadedCount);

        // Папки по-прежнему идут раньше файлов.
        var firstVideoIndex = secondPage.Matches
            .ToList()
            .FindIndex(match => match.IsVideoFile);

        Assert.Equal(50, firstVideoIndex);
    }

    // ---------------------------------------------------------------------
    // Сохранение эталонного порядка сортировки
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Pages_KeepPriorityClassesLengthAndCyrillicCaseOrder()
    {
        var context = await CreateContextAsync();

        await AddNamedFoldersAsync(
            context,
            "Почтовая",
            "Главпочтамт",
            "почтаааааа",
            "почтаяяя",
            "почта-яя",
            "ПОЧТА-ЯЯ",
            "Почтальон",
            "Почта");

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "почта", folderSource: true));

        // Классы приоритета, затем длина, затем побайтовое сравнение кириллицы
        // (NOCASE сворачивает только ASCII, поэтому «ПОЧТА-ЯЯ» идёт раньше
        // «почта-яя»), затем идентификатор строки.
        Assert.Equal(
            [
                "Почта",
                "ПОЧТА-ЯЯ",
                "почта-яя",
                "почтаяяя",
                "Почтальон",
                "почтаааааа",
                "Главпочтамт",
                "Почтовая"
            ],
            page.Matches.Select(match => match.Name).ToArray());
    }

    [Fact]
    public async Task Pages_KeepWordFormSemanticsForSmartAndExactModes()
    {
        var context = await CreateContextAsync();

        await AddNamedFoldersAsync(
            context,
            "Почта",
            "Почты",
            "Почтовый",
            "Почтовая");

        var smartPage = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "почта", folderSource: true));

        // «Умный» режим находит словоформы: «Почта» — точное совпадение,
        // остальные подходят по основе «почт» и получают третий класс
        // приоритета, где порядок задаёт длина имени.
        Assert.Equal(
            ["Почта", "Почты", "Почтовая", "Почтовый"],
            smartPage.Matches.Select(match => match.Name).ToArray());

        var exactPage = await context.PageService.SearchFirstPageAsync(
            CreateQuery(
                context,
                "почта",
                folderSource: true,
                mode: FolderSearchMode.Exact));

        // Точный режим сравнивает подстроку буквально, поэтому остаётся только
        // имя, содержащее «почта» целиком.
        Assert.Equal(
            ["Почта"],
            exactPage.Matches.Select(match => match.Name).ToArray());
    }

    // ---------------------------------------------------------------------
    // Отмена, валидация и неутечка ресурсов
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_001)]
    public async Task SearchFirstPageAsync_InvalidMaximumResultCount_Throws(
        int pageSize)
    {
        var context = await CreateContextAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.PageService.SearchFirstPageAsync(
                CreateQuery(
                    context,
                    "почта",
                    folderSource: true,
                    pageSize: pageSize)));
    }

    [Fact]
    public async Task SearchFirstPageAsync_PreCancelledToken_ThrowsCancellation()
    {
        var context = await CreateContextAsync();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.PageService.SearchFirstPageAsync(
                CreateQuery(context, "почта", folderSource: true),
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task SearchNextPageAsync_PreCancelledToken_ThrowsCancellation()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var firstPage = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.PageService.SearchNextPageAsync(
                firstPage.NextSession,
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task Pages_ReleaseDatabaseResources()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 300);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        while (page.HasMoreResults)
        {
            page = await context.PageService.SearchNextPageAsync(
                page.NextSession);
        }

        // После полного листания сервис не удерживает транзакцию чтения на
        // тестовой базе: запись из другого соединения проходит без ожидания.
        await using var connection = new SqliteConnection(
            BuildConnectionString(
                context,
                SqliteOpenMode.ReadWriteCreate));

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE ResourceProbe (Id INTEGER);
            DROP TABLE ResourceProbe;
            """;

        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void Session_DoesNotHoldMatches()
    {
        // Сессия описывает только позиции и счётчики: если в неё когда-нибудь
        // попадёт коллекция совпадений, требование «не загружать все результаты
        // в память» будет нарушено незаметно.
        var properties = typeof(FolderSearchSession).GetProperties();

        Assert.DoesNotContain(
            properties,
            property =>
                property.PropertyType == typeof(FolderSearchResult) ||
                typeof(IEnumerable<FolderSearchResult>)
                    .IsAssignableFrom(property.PropertyType));
    }

    // ---------------------------------------------------------------------
    // CHANGE-STOP: отпечаток индекса (Шаг 6)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchFirstPageAsync_AssignsOpaqueSessionIdPreservedByWith()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            Assert.NotEqual(Guid.Empty, page.NextSession.SessionId);

            var copied = page.NextSession with
            {
                FolderLoadedCount = 0
            };

            Assert.Equal(
                page.NextSession.SessionId,
                copied.SessionId);
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchFirstPageAsync_EmptyQuery_ReturnsSessionWithoutLease()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 5);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "   ", videoSource: true));

        // У пустой выдачи соединения нет; Guid.Empty отличает её от
        // освобождённой сессии, у которой соединение было.
        Assert.Equal(Guid.Empty, page.NextSession.SessionId);
        Assert.Equal(0, GetActiveLeaseCount(context.PageService));

        await context.PageService.ValidateSessionAsync(
            page.NextSession);

        context.PageService.ReleaseSession(page.NextSession);

        Assert.Equal(0, GetActiveLeaseCount(context.PageService));
    }

    [Fact]
    public async Task ValidateSessionAsync_AfterReleaseOrForUnknownId_Throws()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        Assert.NotEqual(Guid.Empty, page.NextSession.SessionId);

        context.PageService.ReleaseSession(page.NextSession);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.PageService.ValidateSessionAsync(
                page.NextSession));

        var unknownSession = page.NextSession with
        {
            SessionId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.PageService.ValidateSessionAsync(
                unknownSession));
    }

    [Fact]
    public async Task ReleaseSession_ClosesNativeDatabaseHandleWithoutPooling()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        Assert.Equal(1, GetActiveLeaseCount(context.PageService));

        // Подготовка использовала пулируемые соединения репозиториев.
        // Очищаем только их пул; сессионное соединение проверяется отдельно.
        SqliteConnection.ClearPool(
            new SqliteConnection(
                BuildConnectionString(
                    context,
                    SqliteOpenMode.ReadWriteCreate)));

        context.PageService.ReleaseSession(page.NextSession);

        Assert.Equal(0, GetActiveLeaseCount(context.PageService));

        // Никакого ClearPool после ReleaseSession: файл должен открываться
        // эксклюзивно и переименовываться, то есть native handle закрыт.
        await using (var stream = new FileStream(
            context.DatabasePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Assert.True(stream.CanWrite);
        }

        var releasedPath = context.DatabasePath + ".released";

        File.Move(context.DatabasePath, releasedPath);

        Assert.True(File.Exists(releasedPath));
        Assert.False(File.Exists(context.DatabasePath));

        File.Move(releasedPath, context.DatabasePath);
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterInsertBetweenPages_StopsWithIndexChanged()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            Assert.True(page.HasMoreResults);

            await ExecuteWriteAsync(
                context,
                (connection, transaction) =>
                    InsertVideoAsync(
                        context,
                        connection,
                        transaction,
                        GetVideoFolderPath(context, 0),
                        "Фестиваль новый.mxf"));

            var exception =
                await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                    () => context.PageService.SearchNextPageAsync(
                        page.NextSession));

            Assert.Equal(
                page.NextSession.SessionId,
                exception.SessionId);

            // Сессия помечена остановленной: повторная попытка не читает
            // порцию и не может обойти CHANGE-STOP.
            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterDeleteBetweenPages_StopsWithIndexChanged()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            await ExecuteWriteAsync(
                context,
                async (connection, transaction) =>
                {
                    await using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText =
                        """
                        DELETE FROM VideoFiles
                        WHERE Id = (SELECT MIN(Id) FROM VideoFiles);
                        """;

                    await command.ExecuteNonQueryAsync();
                });

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterRenameBetweenPages_StopsWithIndexChanged()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            // Переименование в путях записи — вставка новой строки и удаление
            // старой; число совпадений при этом не меняется.
            var folderPath = GetVideoFolderPath(context, 0);
            var oldPath = Path.Combine(
                folderPath,
                CreateVideoName(0));

            await ExecuteWriteAsync(
                context,
                async (connection, transaction) =>
                {
                    await InsertVideoAsync(
                        context,
                        connection,
                        transaction,
                        folderPath,
                        "Фестиваль переименованный.mxf");

                    await using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText =
                        """
                        DELETE FROM VideoFiles
                        WHERE FullPath = $oldPath;
                        """;

                    command.Parameters.AddWithValue(
                        "$oldPath",
                        oldPath);

                    await command.ExecuteNonQueryAsync();
                });

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterInPlaceUpdateWithUnchangedDiagnosticStamp_Stops()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            // Тройка (COUNT, MAX(Id), MAX(LastSeenUtc)) не меняется: обновление
            // на месте не добавляет и не удаляет строки и не трогает метку.
            var stampBefore =
                await ReadVideoDiagnosticStampAsync(context);

            const string updatedName =
                "Фестиваль сборка обновлённый";

            await ExecuteWriteAsync(
                context,
                async (connection, transaction) =>
                {
                    await using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText =
                        """
                        UPDATE VideoFiles
                        SET Name = $name,
                            NormalizedName = $normalizedName
                        WHERE Id = (SELECT MIN(Id) FROM VideoFiles);
                        """;

                    command.Parameters.AddWithValue(
                        "$name",
                        updatedName);

                    command.Parameters.AddWithValue(
                        "$normalizedName",
                        context.NormalizationService.Normalize(
                            updatedName));

                    await command.ExecuteNonQueryAsync();
                });

            var stampAfter =
                await ReadVideoDiagnosticStampAsync(context);

            Assert.Equal(stampBefore, stampAfter);

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterNetZeroInsertAndDelete_Stops()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            var stampBefore =
                await ReadVideoDiagnosticStampAsync(context);

            var folderPath = GetVideoFolderPath(context, 0);
            var insertedName = "Фестиваль временный.mxf";
            var insertedPath = Path.Combine(
                folderPath,
                insertedName);

            await ExecuteWriteAsync(
                context,
                async (connection, transaction) =>
                {
                    await InsertVideoAsync(
                        context,
                        connection,
                        transaction,
                        folderPath,
                        insertedName);

                    await using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText =
                        """
                        DELETE FROM VideoFiles
                        WHERE FullPath = $fullPath;
                        """;

                    command.Parameters.AddWithValue(
                        "$fullPath",
                        insertedPath);

                    await command.ExecuteNonQueryAsync();
                });

            // Состав строк и все три диагностических значения совпадают с
            // исходными, но data_version уже изменился коммитом.
            var stampAfter =
                await ReadVideoDiagnosticStampAsync(context);

            Assert.Equal(stampBefore, stampAfter);

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task ValidateSessionAsync_AfterWriterCommit_StopsAndMarksSession()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 250);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            await InsertMatchingVideoAsync(
                context,
                "Фестиваль после проверки.mxf");

            var exception =
                await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                    () => context.PageService.ValidateSessionAsync(
                        page.NextSession));

            Assert.Equal(
                page.NextSession.SessionId,
                exception.SessionId);

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task ValidateSessionAsync_WithUnchangedIndex_AllowsNextPage()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            await context.PageService.ValidateSessionAsync(
                page.NextSession);

            var nextPage =
                await context.PageService.SearchNextPageAsync(
                    page.NextSession);

            Assert.Equal(200, nextPage.Matches.Count);

            await context.PageService.ValidateSessionAsync(
                nextPage.NextSession);

            context.PageService.ReleaseSession(
                nextPage.NextSession);
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task ValidateSessionAsync_AfterLastPageBeforeRelease_DetectsCommit()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 100);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            Assert.False(page.HasMoreResults);

            // Последняя страница не освобождает сессию: до публикации
            // соединение обязано оставаться доступным для проверки.
            Assert.Equal(
                1,
                GetActiveLeaseCount(context.PageService));

            await InsertMatchingVideoAsync(
                context,
                "Фестиваль после последней страницы.mxf");

            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.ValidateSessionAsync(
                    page.NextSession));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_AfterOtherConnectionReadsDataVersion_StillSucceeds()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            // Другое соединение имеет собственное значение data_version:
            // сверка обязана остаться на сессионном соединении.
            await using (var otherConnection =
                CreateReadOnlyConnection(context))
            {
                await otherConnection.OpenAsync();

                await using var command =
                    otherConnection.CreateCommand();

                command.CommandText = "PRAGMA data_version;";

                Assert.NotNull(
                    await command.ExecuteScalarAsync());
            }

            var nextPage =
                await context.PageService.SearchNextPageAsync(
                    page.NextSession);

            Assert.Equal(200, nextPage.Matches.Count);

            await context.PageService.ValidateSessionAsync(
                nextPage.NextSession);

            context.PageService.ReleaseSession(
                nextPage.NextSession);
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task ConcurrentWriter_CommitAroundNextPage_IsDetected()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 1_000);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            var writer = Task.Run(
                () => InsertMatchingVideoAsync(
                    context,
                    "Фестиваль параллельный.mxf"));

            var session = page.NextSession;

            try
            {
                var nextPage =
                    await context.PageService.SearchNextPageAsync(
                        session);

                session = nextPage.NextSession;
            }
            catch (FolderSearchIndexChangedException)
            {
                // Коммит писателя обогнал порцию и уже остановил сессию.
            }

            await writer;

            // Независимо от того, успела ли порция прочитаться до коммита,
            // следующая проверка обязана обнаружить изменение.
            await Assert.ThrowsAsync<FolderSearchIndexChangedException>(
                () => context.PageService.SearchNextPageAsync(
                    session));
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task SearchNextPageAsync_CancelledTokenKeepsLeaseUntilRelease()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var page = await context.PageService.SearchFirstPageAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        try
        {
            using var cancellationSource =
                new CancellationTokenSource();

            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.PageService.SearchNextPageAsync(
                    page.NextSession,
                    cancellationSource.Token));

            // Отмена порции не освобождает сессию: вызывающая сторона решает,
            // повторить загрузку или освободить ресурсы.
            Assert.Equal(
                1,
                GetActiveLeaseCount(context.PageService));

            var nextPage =
                await context.PageService.SearchNextPageAsync(
                    page.NextSession);

            Assert.Equal(200, nextPage.Matches.Count);

            context.PageService.ReleaseSession(
                nextPage.NextSession);

            // Повторное освобождение идемпотентно.
            context.PageService.ReleaseSession(
                nextPage.NextSession);

            Assert.Equal(
                0,
                GetActiveLeaseCount(context.PageService));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => context.PageService.SearchNextPageAsync(
                    nextPage.NextSession));

            // После освобождения блокировка чтения не удерживается: запись из
            // другого соединения проходит без ожидания.
            await ExecuteWriteAsync(
                context,
                async (connection, transaction) =>
                {
                    await using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText =
                        """
                        CREATE TABLE ReleaseProbe (Id INTEGER);
                        DROP TABLE ReleaseProbe;
                        """;

                    await command.ExecuteNonQueryAsync();
                });
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }
    }

    [Fact]
    public async Task ReleaseSession_IsIdempotentAndDoesNotCloseOtherSessions()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 1_000);

        var query = CreateQuery(
            context,
            "фестиваль",
            videoSource: true);

        var firstSession =
            await context.PageService.SearchFirstPageAsync(query);

        var secondSession =
            await context.PageService.SearchFirstPageAsync(query);

        try
        {
            Assert.NotEqual(
                firstSession.NextSession.SessionId,
                secondSession.NextSession.SessionId);

            Assert.Equal(
                2,
                GetActiveLeaseCount(context.PageService));

            context.PageService.ReleaseSession(
                firstSession.NextSession);

            context.PageService.ReleaseSession(
                firstSession.NextSession);

            Assert.Equal(
                1,
                GetActiveLeaseCount(context.PageService));

            var nextPage =
                await context.PageService.SearchNextPageAsync(
                    secondSession.NextSession);

            Assert.Equal(200, nextPage.Matches.Count);

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => context.PageService.SearchNextPageAsync(
                    firstSession.NextSession));

            context.PageService.ReleaseSession(
                nextPage.NextSession);
        }
        finally
        {
            context.PageService.ReleaseSession(
                firstSession.NextSession);

            context.PageService.ReleaseSession(
                secondSession.NextSession);
        }
    }

    [Fact]
    public async Task LegacySearchAsync_ReleasesSessionLeaseInFinally()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var results = await context.SearchService.SearchAsync(
            CreateQuery(context, "фестиваль", videoSource: true));

        Assert.Equal(200, results.Count);

        // Точечный поиск не возвращает сессию, поэтому не должен оставлять
        // удерживаемое соединение.
        Assert.Equal(
            0,
            GetActiveLeaseCount(context.PageService));
    }

    [Fact]
    public async Task Dispose_ReleasesActiveSessionLeases()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(context, videoCount: 500);

        var query = CreateQuery(
            context,
            "фестиваль",
            videoSource: true);

        var firstSession =
            await context.PageService.SearchFirstPageAsync(query);

        var secondSession =
            await context.PageService.SearchFirstPageAsync(query);

        Assert.Equal(
            2,
            GetActiveLeaseCount(context.PageService));

        context.PageService.Dispose();

        Assert.Equal(
            0,
            GetActiveLeaseCount(context.PageService));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.PageService.SearchNextPageAsync(
                firstSession.NextSession));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.PageService.SearchNextPageAsync(
                secondSession.NextSession));
    }

    [Fact]
    public async Task Search_WithUnavailableUncSourcePath_ReturnsIndexedMatches()
    {
        var context = await CreateContextAsync();

        var uncRoot = @"\\offline-vaf-test\archive";
        var folderPath = Path.Combine(
            uncRoot,
            "2025",
            "Фестиваль");
        var sourceId = Guid.NewGuid();

        await context.Repository.UpsertBatchAsync(
            [
                CreateFolder(
                    context,
                    folderPath,
                    "Фестиваль сборка",
                    rootSourceId: sourceId)
            ]);

        await context.VideoRepository.UpsertBatchAsync(
            [
                CreateVideo(
                    context,
                    folderPath,
                    sourceId,
                    "10_15_Фестиваль_сборка.mxf")
            ]);

        var query = new FolderSearchQuery(
            "фестиваль",
            FolderSearchMode.Smart,
            DefaultPageSize,
            RootSourceIds: [sourceId],
            VideoFileRootSourceIds: [sourceId]);

        var page = await context.PageService.SearchFirstPageAsync(query);

        try
        {
            // Поиск идёт только по локальному файлу SQLite: недоступный
            // UNC-путь источника не участвует и не проверяется.
            Assert.Equal(1, page.FolderMatchCount);
            Assert.Equal(1, page.VideoMatchCount);
            Assert.Equal(2, page.Matches.Count);

            Assert.Contains(
                page.Matches,
                match => match.Name ==
                    "10_15_Фестиваль_сборка.mxf");
        }
        finally
        {
            context.PageService.ReleaseSession(page.NextSession);
        }

        Assert.True(
            File.Exists(context.DatabasePath));

        Assert.StartsWith(
            _temporaryDirectory,
            context.DatabasePath,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Инфраструктура теста
    // ---------------------------------------------------------------------

    private static FolderSearchQuery CreateQuery(
        SearchTestContext context,
        string queryText,
        bool folderSource = false,
        bool videoSource = false,
        int pageSize = DefaultPageSize,
        FolderSearchMode mode = FolderSearchMode.Smart)
    {
        return new FolderSearchQuery(
            queryText,
            mode,
            pageSize,
            folderSource ? [context.RootSourceId] : [],
            videoSource ? [context.VideoRootSourceId] : []);
    }

    /// <summary>
    /// Независимый эталон: та же область, тот же фильтр и тот же порядок
    /// сортировки, но выборка страницами через <c>OFFSET</c>/<c>LIMIT</c>.
    /// Папки идут первыми — так же, как их ставит бюджет порций.
    /// </summary>
    private static async Task<IReadOnlyList<FolderSearchResult>>
        ReadReferenceMatchesAsync(
            SearchTestContext context,
            FolderSearchQuery query)
    {
        var normalizedQuery =
            context.NormalizationService.Normalize(query.Text);

        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        await using var connection = CreateReadOnlyConnection(context);

        await connection.OpenAsync();

        var folderResults = new List<FolderSearchResult>();
        var rootSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray() ?? [];

        if (rootSourceIds.Length > 0)
        {
            folderResults.AddRange(
                await ReadReferenceStreamAsync(
                    context,
                    connection,
                    query,
                    normalizedQuery,
                    rootSourceIds,
                    isVideoStream: false));
        }

        var videoResults = new List<FolderSearchResult>();
        var videoSourceIds = query.VideoFileRootSourceIds?
            .Distinct()
            .ToArray() ?? [];

        if (videoSourceIds.Length > 0)
        {
            videoResults.AddRange(
                await ReadReferenceStreamAsync(
                    context,
                    connection,
                    query,
                    normalizedQuery,
                    videoSourceIds,
                    isVideoStream: true));
        }

        return folderResults
            .Concat(videoResults)
            .ToArray();
    }

    private static async Task<IReadOnlyList<FolderSearchResult>>
        ReadReferenceStreamAsync(
            SearchTestContext context,
            SqliteConnection connection,
            FolderSearchQuery query,
            string normalizedQuery,
            IReadOnlyList<Guid> rootSourceIds,
            bool isVideoStream)
    {
        var results = new List<FolderSearchResult>();
        var offset = 0;

        while (true)
        {
            await using var command = connection.CreateCommand();

            var searchCondition = isVideoStream
                ? FolderSearchSql.BuildVideoSearchCondition(
                    command,
                    context.NormalizationService,
                    context.StemService,
                    query)
                : FolderSearchSql.BuildFolderSearchCondition(
                    command,
                    context.NormalizationService,
                    context.StemService,
                    query);

            command.CommandText = isVideoStream
                ? FolderSearchSql.BuildVideoOffsetCommand(
                    searchCondition,
                    rootSourceIds)
                : FolderSearchSql.BuildFolderOffsetCommand(
                    searchCondition,
                    rootSourceIds);

            command.Parameters.AddWithValue(
                "$normalizedQuery",
                normalizedQuery);

            command.Parameters.AddWithValue(
                "$pageSize",
                query.MaxResults);

            command.Parameters.AddWithValue("$offset", offset);

            FolderSearchSql.AddRootSourceIdParameters(
                command,
                rootSourceIds,
                isVideoStream
                    ? "videoRootSourceId"
                    : "rootSourceId");

            var pageCount = 0;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(
                    isVideoStream
                        ? FolderSearchSql.ReadVideoResult(reader)
                        : FolderSearchSql.ReadResult(reader));

                pageCount++;
            }

            if (pageCount < query.MaxResults)
            {
                return results;
            }

            offset += query.MaxResults;
        }
    }

    private async Task AddFoldersAsync(
        SearchTestContext context,
        int count)
    {
        var folders = Enumerable
            .Range(0, count)
            .Select(index => CreateFolder(
                context,
                Path.Combine(
                    _temporaryDirectory,
                    "Папочный источник",
                    $"Папка {index:D5}"),
                $"Почта фестиваль сборка {index:D4}"))
            .ToArray();

        await context.Repository.UpsertBatchAsync(folders);
    }

    private async Task AddNamedFoldersAsync(
        SearchTestContext context,
        params string[] names)
    {
        var folders = names
            .Select((name, index) => CreateFolder(
                context,
                Path.Combine(
                    _temporaryDirectory,
                    "Папочный источник",
                    $"Папка {index:D3} {name}"),
                name))
            .ToArray();

        await context.Repository.UpsertBatchAsync(folders);
    }

    /// <summary>
    /// Создаёт видеофайлы с разнообразными именами: часть совпадает с началом
    /// имени (класс 1), часть — в середине (класс 2). Имена уникальны, но внутри
    /// класса много одинаковых длин, поэтому порядок задают и длина, и имя.
    /// Все имена содержат и «фестиваль», и «сборка» — так один и тот же набор
    /// подходит и для односложного, и для двусложного запроса.
    /// </summary>
    private async Task AddVideoFilesAsync(
        SearchTestContext context,
        int videoCount)
    {
        var folderCount = 21;

        await EnsureVideoFoldersAsync(context, folderCount);

        var files = new List<VideoFileIndexUpsertItem>(videoCount);

        for (var index = 0; index < videoCount; index++)
        {
            var folderPath = GetVideoFolderPath(context, index % folderCount);

            files.Add(CreateVideo(
                context,
                folderPath,
                context.VideoRootSourceId,
                CreateVideoName(index)));
        }

        await context.VideoRepository.UpsertBatchAsync(files);
    }

    private static string CreateVideoName(int index)
    {
        return (index % 3) switch
        {
            0 => $"Фестиваль сборка {index:D5}.mxf",
            1 => $"{index % 28 + 1:D2}_{index:D5}_ФестивальСборка.mxf",
            _ => $"Фестиваль_сборка_материал_{index:D5}.mxf"
        };
    }

    /// <summary>
    /// Видеофайлы с одинаковыми именами в разных папках: ключ сортировки
    /// совпадает полностью, и порядок задаёт только <c>Id</c>.
    /// </summary>
    private async Task AddIdenticalNamedVideoFilesAsync(
        SearchTestContext context,
        int count)
    {
        await EnsureVideoFoldersAsync(context, count);

        var files = Enumerable
            .Range(0, count)
            .Select(index => CreateVideo(
                context,
                GetVideoFolderPath(context, index),
                context.VideoRootSourceId,
                "Фестиваль.mxf"))
            .ToArray();

        await context.VideoRepository.UpsertBatchAsync(files);
    }

    private async Task EnsureVideoFoldersAsync(
        SearchTestContext context,
        int folderCount)
    {
        if (context.VideoFolderCount >= folderCount)
        {
            return;
        }

        var folders = Enumerable
            .Range(
                context.VideoFolderCount,
                folderCount - context.VideoFolderCount)
            .Select(index => CreateFolder(
                context,
                GetVideoFolderPath(context, index),
                $"Фестиваль сборка видео {index:D3}",
                rootSourceId: context.VideoRootSourceId))
            .ToArray();

        await context.Repository.UpsertBatchAsync(folders);

        context.VideoFolderCount = folderCount;
    }

    private static string GetVideoFolderPath(
        SearchTestContext context,
        int index)
    {
        return Path.Combine(
            context.VideoRootPath,
            $"Папка {index:D3}");
    }

    private async Task<SearchTestContext> CreateContextAsync(
        bool initializeDatabase = true)
    {
        var directoryProvider =
            new TestApplicationDataDirectoryProvider(
                _temporaryDirectory);

        var databasePathProvider =
            new IndexDatabasePathProvider(
                directoryProvider);

        if (initializeDatabase)
        {
            var initializer =
                new SqliteIndexDatabaseInitializer(
                    databasePathProvider,
                    NullLogger<
                        SqliteIndexDatabaseInitializer>.Instance);

            await initializer.InitializeAsync();
        }

        var repository =
            new SqliteFolderIndexRepository(
                databasePathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        var videoRepository =
            new SqliteVideoFileIndexRepository(
                databasePathProvider,
                NullLogger<
                    SqliteVideoFileIndexRepository>.Instance);

        var normalizationService =
            new TextNormalizationService();

        var stemService =
            new RussianSearchStemService();

        var pageService =
            new SqliteFolderSearchPageService(
                databasePathProvider,
                normalizationService,
                stemService,
                NullLogger<
                    SqliteFolderSearchPageService>.Instance);

        _pageServices.Add(pageService);

        var searchService =
            new SqliteFolderSearchService(pageService);

        return new SearchTestContext(
            repository,
            videoRepository,
            pageService,
            searchService,
            normalizationService,
            stemService,
            Guid.NewGuid(),
            Guid.NewGuid(),
            directoryProvider,
            Path.Combine(
                _temporaryDirectory,
                "Файловый источник"),
            databasePathProvider.GetDatabasePath());
    }

    private static FolderIndexUpsertItem CreateFolder(
        SearchTestContext context,
        string fullPath,
        string name,
        Guid? rootSourceId = null)
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
            ParentFullPath: null,
            RootSourceId:
                rootSourceId ?? context.RootSourceId,
            IsAvailable: true,
            LastSeenUtc: DateTimeOffset.UtcNow,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static VideoFileIndexUpsertItem CreateVideo(
        SearchTestContext context,
        string folderFullPath,
        Guid rootSourceId,
        string name)
    {
        return new VideoFileIndexUpsertItem(
            FullPath: Path.Combine(folderFullPath, name),
            Name: name,
            NormalizedName:
                context.NormalizationService.Normalize(name),
            Extension: Path.GetExtension(name),
            SizeBytes: 1_000,
            LastWriteTimeUtc: DateTimeOffset.UtcNow,
            FolderFullPath: folderFullPath,
            RootSourceId: rootSourceId,
            IsAvailable: true,
            LastSeenUtc: DateTimeOffset.UtcNow);
    }

    private static SqliteConnection CreateReadOnlyConnection(
        SearchTestContext context)
    {
        return new SqliteConnection(
            BuildConnectionString(
                context,
                SqliteOpenMode.ReadOnly));
    }

    private static string BuildConnectionString(
        SearchTestContext context,
        SqliteOpenMode mode)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(
                context.DirectoryProvider
                    .GetApplicationDataDirectory(),
                "Index",
                "video-archive-finder.db"),

            Mode = mode,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    private static async Task ExecuteWriteAsync(
        SearchTestContext context,
        Func<SqliteConnection, SqliteTransaction, Task> write)
    {
        await using var connection = new SqliteConnection(
            BuildConnectionString(
                context,
                SqliteOpenMode.ReadWriteCreate));

        await connection.OpenAsync();

        await using (var configuration =
            connection.CreateCommand())
        {
            configuration.CommandText =
                "PRAGMA busy_timeout = 5000;";

            await configuration.ExecuteNonQueryAsync();
        }

        await using var transaction =
            connection.BeginTransaction();

        await write(connection, transaction);

        await transaction.CommitAsync();
    }

    private static async Task InsertMatchingVideoAsync(
        SearchTestContext context,
        string name)
    {
        await ExecuteWriteAsync(
            context,
            (connection, transaction) =>
                InsertVideoAsync(
                    context,
                    connection,
                    transaction,
                    GetVideoFolderPath(context, 0),
                    name));
    }

    private static async Task InsertVideoAsync(
        SearchTestContext context,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string folderFullPath,
        string name)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText =
            """
            INSERT INTO VideoFiles
            (
                FullPath,
                Name,
                NormalizedName,
                Extension,
                SizeBytes,
                LastWriteTimeUtc,
                FolderFullPath,
                RootSourceId,
                IsAvailable,
                LastSeenUtc
            )
            VALUES
            (
                $fullPath,
                $name,
                $normalizedName,
                $extension,
                $sizeBytes,
                $lastWriteTimeUtc,
                $folderFullPath,
                $rootSourceId,
                1,
                $lastSeenUtc
            );
            """;

        var timestamp = DateTimeOffset.UtcNow;

        command.Parameters.AddWithValue(
            "$fullPath",
            Path.Combine(folderFullPath, name));

        command.Parameters.AddWithValue("$name", name);

        command.Parameters.AddWithValue(
            "$normalizedName",
            context.NormalizationService.Normalize(name));

        command.Parameters.AddWithValue(
            "$extension",
            Path.GetExtension(name));

        command.Parameters.AddWithValue("$sizeBytes", 1_000);

        command.Parameters.AddWithValue(
            "$lastWriteTimeUtc",
            timestamp.ToString("O"));

        command.Parameters.AddWithValue(
            "$folderFullPath",
            folderFullPath);

        command.Parameters.AddWithValue(
            "$rootSourceId",
            context.VideoRootSourceId.ToString("D"));

        command.Parameters.AddWithValue(
            "$lastSeenUtc",
            timestamp.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<
        (long Count, long MaxId, string MaxLastSeenUtc)>
        ReadVideoDiagnosticStampAsync(
            SearchTestContext context)
    {
        await using var connection =
            CreateReadOnlyConnection(context);

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                COUNT(*),
                COALESCE(MAX(Id), 0),
                COALESCE(MAX(LastSeenUtc), '')
            FROM VideoFiles;
            """;

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return (
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2));
    }

    private static int GetActiveLeaseCount(
        SqliteFolderSearchPageService pageService)
    {
        var field = typeof(SqliteFolderSearchPageService)
            .GetField(
                "_leases",
                BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var leases = field.GetValue(pageService);

        Assert.NotNull(leases);

        var count = leases.GetType()
            .GetProperty("Count")?
            .GetValue(leases);

        return count is int value
            ? value
            : throw new InvalidOperationException(
                "Could not read the active lease count.");
    }

    public void Dispose()
    {
        // Каждый тест создаёт собственный сервис; закрываем удерживаемые
        // сессионные соединения до очистки пула и временного каталога.
        foreach (var pageService in _pageServices)
        {
            pageService.Dispose();
        }

        // Освобождается только пул этой тестовой базы: глобальный
        // SqliteConnection.ClearAllPools() затрагивает соединения других
        // параллельно выполняющихся тестов.
        SqliteConnection.ClearPool(
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(
                        _temporaryDirectory,
                        "Index",
                        "video-archive-finder.db"),

                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString()));

        SqliteConnection.ClearPool(
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(
                        _temporaryDirectory,
                        "Index",
                        "video-archive-finder.db"),

                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString()));

        DeleteTemporaryDirectory();
    }

    private void DeleteTemporaryDirectory()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(_temporaryDirectory))
                {
                    return;
                }

                Directory.Delete(
                    _temporaryDirectory,
                    recursive: true);

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class SearchTestContext(
        SqliteFolderIndexRepository repository,
        SqliteVideoFileIndexRepository videoRepository,
        SqliteFolderSearchPageService pageService,
        SqliteFolderSearchService searchService,
        ITextNormalizationService normalizationService,
        ISearchStemService stemService,
        Guid rootSourceId,
        Guid videoRootSourceId,
        IApplicationDataDirectoryProvider directoryProvider,
        string videoRootPath,
        string databasePath)
    {
        public SqliteFolderIndexRepository Repository
        {
            get;
        } = repository;

        public SqliteVideoFileIndexRepository VideoRepository
        {
            get;
        } = videoRepository;

        public SqliteFolderSearchPageService PageService
        {
            get;
        } = pageService;

        public SqliteFolderSearchService SearchService
        {
            get;
        } = searchService;

        public ITextNormalizationService NormalizationService
        {
            get;
        } = normalizationService;

        public ISearchStemService StemService
        {
            get;
        } = stemService;

        public Guid RootSourceId
        {
            get;
        } = rootSourceId;

        public Guid VideoRootSourceId
        {
            get;
        } = videoRootSourceId;

        public IApplicationDataDirectoryProvider DirectoryProvider
        {
            get;
        } = directoryProvider;

        public string VideoRootPath
        {
            get;
        } = videoRootPath;

        public string DatabasePath
        {
            get;
        } = databasePath;

        public int VideoFolderCount { get; set; }
    }

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
