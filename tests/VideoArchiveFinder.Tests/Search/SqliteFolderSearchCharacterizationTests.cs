using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.Tests.Search;

/// <summary>
/// Характеризующие тесты исходного поведения поиска (Шаг 0 плана из
/// <c>docs/SEARCH_COMPLETENESS_AUDIT.md</c>).
///
/// Тесты намеренно фиксируют <b>текущее</b> поведение, а не желаемое: они должны
/// проходить на существующей реализации и служить эталоном, относительно которого
/// будет проверяться постраничная выдача. Если тест из этого файла начинает падать,
/// значит изменилось наблюдаемое поведение поиска, и это требует осознанного решения.
///
/// Зафиксировано:
/// <list type="number">
/// <item>усечение выдачи до запрошенного максимума (250 совпадений → 200);</item>
/// <item>объединение совпадений по папкам и по видеофайлам с приоритетом папок и
/// общим бюджетом выдачи;</item>
/// <item>предел <c>MaximumAllowedResults = 1000</c> и валидация <c>MaxResults</c>;</item>
/// <item>эталонный порядок сортировки
/// <c>Priority, length(Name), Name COLLATE NOCASE, Id</c>, включая кириллицу,
/// различный регистр и равные ключи сортировки.</item>
/// </list>
/// </summary>
public sealed class SqliteFolderSearchCharacterizationTests
    : IDisposable
{
    /// <summary>
    /// Значение <c>FolderSearchViewModel.MaximumDisplayedResults</c> — размер выдачи,
    /// который модель представления запрашивает у сервиса.
    /// </summary>
    private const int DisplayedResultsLimit = 200;

    /// <summary>
    /// Значение <c>SqliteFolderSearchService.MaximumAllowedResults</c> — «архитектурный»
    /// потолок сервиса, выше которого запрос отвергается.
    /// </summary>
    private const int AllowedResultsLimit = 1000;

    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    // ---------------------------------------------------------------------
    // 1. Усечение выдачи
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_250MatchingVideoFiles_TruncatesTo200()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(
            context,
            count: 250);

        var results = await SearchAsync(
            context,
            "фестиваль",
            DisplayedResultsLimit,
            folderSourceId: null,
            videoSourceId: context.VideoRootSourceId);

        Assert.Equal(DisplayedResultsLimit, results.Count);
    }

    [Fact]
    public async Task SearchAsync_250MatchingFolders_TruncatesTo200()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            count: 250);

        var results = await SearchAsync(
            context,
            "почта",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(DisplayedResultsLimit, results.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(199, 199)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(249, 200)]
    [InlineData(250, 200)]
    public async Task SearchAsync_TruncatesExactlyAtDisplayedLimit(
        int matchCount,
        int expectedCount)
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(
            context,
            count: matchCount);

        var results = await SearchAsync(
            context,
            "фестиваль",
            DisplayedResultsLimit,
            folderSourceId: null,
            videoSourceId: context.VideoRootSourceId);

        Assert.Equal(expectedCount, results.Count);
    }

    [Fact]
    public async Task SearchAsync_TruncationKeepsHeadOfReferenceOrder()
    {
        var context = await CreateContextAsync();

        await AddVideoFilesAsync(
            context,
            count: 250);

        var results = await SearchAsync(
            context,
            "фестиваль",
            DisplayedResultsLimit,
            folderSourceId: null,
            videoSourceId: context.VideoRootSourceId);

        // Имена строятся как «Фестиваль сборка 0000.mxf», «Фестиваль сборка 0001.mxf», …
        // — одна длина, один класс приоритета, поэтому порядок задаёт Name, а усечение
        // обязано сохранить начало эталонного порядка, а не случайные строки.
        Assert.Equal("Фестиваль сборка 0000.mxf", results[0].Name);
        Assert.Equal("Фестиваль сборка 0199.mxf", results[^1].Name);
    }

    // ---------------------------------------------------------------------
    // 2. Объединение папок и видеофайлов, приоритет папок
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WhenFoldersAloneFillBudget_ReturnsOnlyFolders()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            count: 250);

        await AddVideoFilesAsync(
            context,
            count: 250);

        var results = await SearchAsync(
            context,
            "фестиваль сборка",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: context.VideoRootSourceId);

        // Оба потока упорядочены независимо и каждый ограничен MaxResults, но
        // объединённая выдача ограничена тем же бюджетом, а папки идут первыми —
        // значит при 250 совпадениях по папкам до видеофайлов очередь не доходит.
        Assert.Equal(DisplayedResultsLimit, results.Count);
        Assert.All(
            results,
            result => Assert.False(result.IsVideoFile));
    }

    [Fact]
    public async Task SearchAsync_FoldersComeFirstAndVideoFilesUseRemainingBudget()
    {
        var context = await CreateContextAsync();

        const int folderMatchCount = 30;

        await AddFoldersAsync(
            context,
            count: folderMatchCount);

        await AddVideoFilesAsync(
            context,
            count: 250);

        var results = await SearchAsync(
            context,
            "фестиваль сборка",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: context.VideoRootSourceId);

        Assert.Equal(DisplayedResultsLimit, results.Count);

        Assert.Equal(
            folderMatchCount,
            results.Count(result => !result.IsVideoFile));

        Assert.Equal(
            DisplayedResultsLimit - folderMatchCount,
            results.Count(result => result.IsVideoFile));

        // Приоритет папок: ни один видеофайл не идёт раньше папки.
        Assert.All(
            results.Take(folderMatchCount),
            result => Assert.False(result.IsVideoFile));

        Assert.All(
            results.Skip(folderMatchCount),
            result => Assert.True(result.IsVideoFile));
    }

    [Fact]
    public async Task SearchAsync_FolderAndVideoMatchesKeepTheirSourceScope()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            count: 1);

        await AddVideoFilesAsync(
            context,
            count: 1);

        var results = await SearchAsync(
            context,
            "фестиваль сборка",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: context.VideoRootSourceId);

        var folder = Assert.Single(
            results,
            result => !result.IsVideoFile);

        var video = Assert.Single(
            results,
            result => result.IsVideoFile);

        Assert.Equal(context.RootSourceId, folder.RootSourceId);
        Assert.Equal(context.VideoRootSourceId, video.RootSourceId);
        Assert.Equal(context.VideoFolderPath, video.NavigationFolderFullPath);
        Assert.NotNull(video.NavigationFolderId);
    }

    // ---------------------------------------------------------------------
    // 3. Предел 1000 результатов и валидация
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithMoreThanAllowedResults_ReturnsExactly1000()
    {
        var context = await CreateContextAsync();

        await AddFoldersAsync(
            context,
            count: AllowedResultsLimit + 1);

        var results = await SearchAsync(
            context,
            "почта",
            AllowedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        // MaxResults = 1000 допустим и не отвергается, но выше его выдача не растёт.
        Assert.Equal(AllowedResultsLimit, results.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AllowedResultsLimit + 1)]
    public async Task SearchAsync_InvalidMaximumResultCount_Throws(
        int maxResults)
    {
        var context = await CreateContextAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.SearchService.SearchAsync(
                new FolderSearchQuery(
                    "почта",
                    FolderSearchMode.Smart,
                    maxResults,
                    RootSourceIds: [context.RootSourceId])));
    }

    // ---------------------------------------------------------------------
    // 4. Эталонный порядок сортировки
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_OrdersFoldersByPriorityClasses()
    {
        var context = await CreateContextAsync();

        // Класс 0 — нормализованное имя равно запросу;
        // класс 1 — запрос стоит в начале имени;
        // класс 2 — запрос встречается внутри имени;
        // класс 3 — имя подошло только по токенам или основам слова.
        await AddNamedFoldersAsync(
            context,
            "Почтовая",
            "Главпочтамт",
            "Почтальон",
            "Почта");

        var results = await SearchAsync(
            context,
            "почта",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(
            ["Почта", "Почтальон", "Главпочтамт", "Почтовая"],
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task SearchAsync_OrdersByLengthWithinSamePriorityClass()
    {
        var context = await CreateContextAsync();

        // Оба имени имеют класс 1, поэтому порядок обязан задавать length(Name).
        // Специально подобрано так, чтобы правила расходились: по алфавиту
        // «почтаааааа» идёт раньше «почтаяяя» (0x430 «а» < 0x44F «я»), а по длине —
        // наоборот. Имена вставлены так, что совпадение с порядком вставки исключено.
        await AddNamedFoldersAsync(
            context,
            "почтаааааа",
            "почтаяяя");

        var results = await SearchAsync(
            context,
            "почта",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(
            ["почтаяяя", "почтаааааа"],
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task SearchAsync_DoesNotFoldCyrillicCase()
    {
        var context = await CreateContextAsync();

        // Встроенная коллация SQLite NOCASE сворачивает только ASCII, поэтому для
        // кириллицы сравнение фактически побайтовое и заглавная буква идёт первой.
        // Первое имя вставлено раньше и имеет меньший Id — если бы регистр
        // сворачивался, порядок был бы обратным.
        await AddNamedFoldersAsync(
            context,
            "почта-яя",
            "ПОЧТА-ЯЯ");

        var results = await SearchAsync(
            context,
            "почта",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(
            ["ПОЧТА-ЯЯ", "почта-яя"],
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task SearchAsync_FoldsAsciiCaseAndFallsBackToId()
    {
        var context = await CreateContextAsync();

        // Для ASCII регистр сворачивается, ключи сортировки становятся равными и
        // порядок задаёт Id по возрастанию: первое вставленное имя идёт первым,
        // хотя побайтовое сравнение поставило бы «FESTIVAL» впереди.
        await AddNamedFoldersAsync(
            context,
            "festival",
            "FESTIVAL");

        var results = await SearchAsync(
            context,
            "festival",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(
            ["festival", "FESTIVAL"],
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task SearchAsync_EqualSortKeys_FallBackToAscendingId()
    {
        var context = await CreateContextAsync();

        var firstPath = Path.Combine(
            _temporaryDirectory,
            "Первый источник",
            "Почта");

        var secondPath = Path.Combine(
            _temporaryDirectory,
            "Второй источник",
            "Почта");

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(context, firstPath, "Почта"),
            CreateFolder(context, secondPath, "Почта")
        ]);

        var results = await SearchAsync(
            context,
            "почта",
            DisplayedResultsLimit,
            folderSourceId: context.RootSourceId,
            videoSourceId: null);

        Assert.Equal(
            [firstPath, secondPath],
            results.Select(result => result.FullPath).ToArray());
    }

    [Fact]
    public async Task SearchAsync_OrdersVideoFilesBySameKey()
    {
        var context = await CreateContextAsync();

        // Имена вставлены в перемешанном порядке: ожидаемый порядок задаёт ключ
        // сортировки, а не порядок вставки. Класс 1 — запрос в начале имени,
        // класс 2 — запрос внутри имени; внутри класса короче имя идёт первым.
        await AddNamedVideoFilesAsync(
            context,
            "10_15_ФестивальПамяти.mxf",
            "Фестиваль.mxf",
            "05_Фестиваль.mxf",
            "Фестиваль_Памяти.mxf");

        var results = await SearchAsync(
            context,
            "фестиваль",
            DisplayedResultsLimit,
            folderSourceId: null,
            videoSourceId: context.VideoRootSourceId);

        Assert.Equal(
            [
                "Фестиваль.mxf",
                "Фестиваль_Памяти.mxf",
                "05_Фестиваль.mxf",
                "10_15_ФестивальПамяти.mxf"
            ],
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task SearchAsync_ExactMatchClass_ComesFirstForVideoFiles()
    {
        var context = await CreateContextAsync();

        await AddNamedVideoFilesAsync(
            context,
            "00_Фестиваль.mxf",
            "Фестиваль.mxf");

        var results = await SearchAsync(
            context,
            "фестиваль.mxf",
            DisplayedResultsLimit,
            folderSourceId: null,
            videoSourceId: context.VideoRootSourceId,
            mode: FolderSearchMode.Exact);

        // Совпало два файла, но имя, нормализованное в точности как запрос,
        // получает класс 0 и идёт первым, хотя вставлено позже.
        Assert.Equal(
            ["Фестиваль.mxf", "00_Фестиваль.mxf"],
            results.Select(result => result.Name).ToArray());
    }

    // ---------------------------------------------------------------------
    // Инфраструктура теста
    // ---------------------------------------------------------------------

    private static Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        SearchTestContext context,
        string queryText,
        int maxResults,
        Guid? folderSourceId,
        Guid? videoSourceId,
        FolderSearchMode mode = FolderSearchMode.Smart)
    {
        return context.SearchService.SearchAsync(
            new FolderSearchQuery(
                queryText,
                mode,
                maxResults,
                folderSourceId is null ? [] : [folderSourceId.Value],
                videoSourceId is null ? [] : [videoSourceId.Value]));
    }

    /// <summary>
    /// Создаёт <paramref name="count"/> папок, имена которых совпадают и с «почта»,
    /// и с двумя словами «фестиваль сборка» — это позволяет использовать одни и те же
    /// папки и в тестах усечения, и в тестах объединения с видеофайлами.
    /// </summary>
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
                    $"Папка {index:D4}"),
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
    /// Создаёт папки файлового источника и <paramref name="count"/> видеофайлов, имена
    /// которых совпадают и с «фестиваль», и с двумя словами «фестиваль сборка».
    /// </summary>
    private async Task AddVideoFilesAsync(
        SearchTestContext context,
        int count)
    {
        await EnsureVideoFoldersAsync(context);

        var files = Enumerable
            .Range(0, count)
            .Select(index => CreateVideo(
                context,
                context.VideoFolderPath,
                context.VideoRootSourceId,
                $"Фестиваль сборка {index:D4}.mxf"))
            .ToArray();

        await context.VideoRepository.UpsertBatchAsync(files);
    }

    private async Task AddNamedVideoFilesAsync(
        SearchTestContext context,
        params string[] names)
    {
        await EnsureVideoFoldersAsync(context);

        var files = names
            .Select(name => CreateVideo(
                context,
                context.VideoFolderPath,
                context.VideoRootSourceId,
                name))
            .ToArray();

        await context.VideoRepository.UpsertBatchAsync(files);
    }

    /// <summary>
    /// Создаёт папку файлового источника: поиск по видеофайлам соединяет
    /// <c>VideoFiles</c> с <c>Folders</c> по пути папки, поэтому без строки папки
    /// совпадение не будет найдено.
    /// </summary>
    private async Task EnsureVideoFoldersAsync(
        SearchTestContext context)
    {
        if (context.VideoFolderCreated)
        {
            return;
        }

        await context.Repository.UpsertBatchAsync(
        [
            CreateFolder(
                context,
                context.VideoFolderPath,
                "Фестиваль сборка видео",
                rootSourceId: context.VideoRootSourceId)
        ]);

        context.VideoFolderCreated = true;
    }

    private async Task<SearchTestContext> CreateContextAsync()
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

        var videoRepository =
            new SqliteVideoFileIndexRepository(
                databasePathProvider,
                NullLogger<
                    SqliteVideoFileIndexRepository>.Instance);

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

        var videoFolderPath = Path.Combine(
            _temporaryDirectory,
            "Файловый источник",
            "Фестиваль сборка");

        return new SearchTestContext(
            repository,
            videoRepository,
            searchService,
            normalizationService,
            stemService,
            Guid.NewGuid(),
            Guid.NewGuid(),
            videoFolderPath);
    }

    private static FolderIndexUpsertItem CreateFolder(
        SearchTestContext context,
        string fullPath,
        string name,
        string? parentFullPath = null,
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
            ParentFullPath: parentFullPath,
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

    public void Dispose()
    {
        ReleaseDatabasePool();
        DeleteTemporaryDirectory();
    }

    /// <summary>
    /// Освобождает пул соединений только этой тестовой базы.
    ///
    /// Глобальный <c>SqliteConnection.ClearAllPools()</c> закрывает пулы всех баз
    /// процесса; при параллельном выполнении тестовых классов он попадал в
    /// соединение, которое в этот момент использовал другой тест, и приводил к
    /// <c>ObjectDisposedException</c> в посторонних тестах. Поэтому здесь
    /// освобождается ровно один пул — по строке соединения этой базы.
    /// </summary>
    private void ReleaseDatabasePool()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(
                    _temporaryDirectory,
                    "Index",
                    "video-archive-finder.db"),

                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();

        using var connection =
            new SqliteConnection(connectionString);

        SqliteConnection.ClearPool(connection);
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
        SqliteFolderSearchService searchService,
        ITextNormalizationService normalizationService,
        ISearchStemService stemService,
        Guid rootSourceId,
        Guid videoRootSourceId,
        string videoFolderPath)
    {
        public SqliteFolderIndexRepository Repository
        {
            get;
        } = repository;

        public SqliteVideoFileIndexRepository VideoRepository
        {
            get;
        } = videoRepository;

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

        public string VideoFolderPath
        {
            get;
        } = videoFolderPath;

        public bool VideoFolderCreated { get; set; }
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
