using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.SearchBenchmark;

internal sealed class PagingSearchBenchmarkRunner
{
    private const int FolderCount = 400;
    private const int VideoFileCount = 376_342;
    private const int BatchSize = 1_000;
    private const int ProgressInterval = 25_000;
    private const int PageSize = 200;
    private const int WarmupIterationCount = 3;
    private const int MeasurementIterationCount = 15;
    private const int OraclePageSize = 1_000_000;
    private const int SelectiveInterval = 313;
    private const double SearchP95ThresholdMilliseconds = 100d;
    private const string BenchmarkVersion = "paging-v1";

    private const string BroadQueryText = "фестиваль";
    private const string SelectiveQueryText = "лабецкая";

    private static readonly Guid SourceAId =
        Guid.Parse("a0000000-0000-4000-8000-000000000001");

    private static readonly Guid SourceBId =
        Guid.Parse("a0000000-0000-4000-8000-000000000002");

    private static readonly DateTimeOffset FixedTimestamp =
        new(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

    private readonly TemporaryApplicationDataDirectoryProvider
        _dataDirectoryProvider;

    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly SqliteIndexDatabaseInitializer
        _databaseInitializer;

    private readonly SqliteFolderIndexRepository
        _folderRepository;

    private readonly SqliteVideoFileIndexRepository
        _videoRepository;

    private readonly ITextNormalizationService
        _normalizationService;

    private readonly ISearchStemService
        _stemService;

    public PagingSearchBenchmarkRunner(
        TemporaryApplicationDataDirectoryProvider
            dataDirectoryProvider)
    {
        _dataDirectoryProvider = dataDirectoryProvider;

        _databasePathProvider =
            new IndexDatabasePathProvider(
                _dataDirectoryProvider);

        _databaseInitializer =
            new SqliteIndexDatabaseInitializer(
                _databasePathProvider,
                NullLogger<
                    SqliteIndexDatabaseInitializer>.Instance);

        _folderRepository =
            new SqliteFolderIndexRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        _videoRepository =
            new SqliteVideoFileIndexRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteVideoFileIndexRepository>.Instance);

        _normalizationService =
            new TextNormalizationService();

        _stemService =
            new RussianSearchStemService();
    }

    public async Task RunAsync(
        CancellationToken cancellationToken = default)
    {
        PrintEnvironmentInformation();

        await _databaseInitializer.InitializeAsync(
            cancellationToken);

        var populationStopwatch = Stopwatch.StartNew();

        await PopulateIndexAsync(cancellationToken);

        populationStopwatch.Stop();

        var databasePath =
            _databasePathProvider.GetDatabasePath();

        var databaseSizeBytes =
            new FileInfo(databasePath).Length;

        var counts =
            await ReadIndexCountsAsync(
                cancellationToken);

        if (counts.FolderCount != FolderCount ||
            counts.VideoFileCount != VideoFileCount)
        {
            throw new InvalidOperationException(
                $"Синтетический индекс содержит " +
                $"{counts.FolderCount:N0} папок и " +
                $"{counts.VideoFileCount:N0} видеофайлов; " +
                $"ожидалось {FolderCount:N0} и " +
                $"{VideoFileCount:N0}. Проверьте уникальность путей.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Создан синтетический индекс: " +
            $"{FolderCount:N0} папок, " +
            $"{VideoFileCount:N0} видеофайлов");

        Console.WriteLine(
            $"Время заполнения: " +
            $"{populationStopwatch.Elapsed.TotalSeconds:N2} с");

        Console.WriteLine(
            $"Размер базы: " +
            $"{databaseSizeBytes / 1024d / 1024d:N2} МБ");

        Console.WriteLine(
            $"Временная база: {databasePath}");

        Console.WriteLine();
        Console.WriteLine(
            $"Прогревов для каждого замера: " +
            $"{WarmupIterationCount}");

        Console.WriteLine(
            $"Измерений для каждого замера: " +
            $"{MeasurementIterationCount}");

        var dataVersionTimings =
            await MeasureDataVersionAsync(
                cancellationToken);

        PrintTiming(
            "PRAGMA data_version",
            dataVersionTimings,
            applySearchThreshold: false);

        using var pageService =
            new SqliteFolderSearchPageService(
                _databasePathProvider,
                _normalizationService,
                _stemService,
                NullLogger<
                    SqliteFolderSearchPageService>.Instance);

        foreach (var benchmarkCase in CreateBenchmarkCases())
        {
            await MeasureCaseAsync(
                pageService,
                benchmarkCase,
                cancellationToken);
        }

        Console.WriteLine();
        Console.WriteLine(
            "Все сценарии завершены. Временная база будет удалена.");
    }

    private async Task PopulateIndexAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Заполнение временного синтетического индекса...");

        for (var firstNumber = 0;
             firstNumber < FolderCount;
             firstNumber += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastNumber = Math.Min(
                firstNumber + BatchSize,
                FolderCount);

            var batch =
                new List<FolderIndexUpsertItem>(
                    lastNumber - firstNumber);

            for (var folderNumber = firstNumber;
                 folderNumber < lastNumber;
                 folderNumber++)
            {
                batch.Add(
                    CreateFolder(folderNumber));
            }

            await _folderRepository.UpsertBatchAsync(
                batch,
                cancellationToken);
        }

        for (var firstNumber = 0;
             firstNumber < VideoFileCount;
             firstNumber += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastNumber = Math.Min(
                firstNumber + BatchSize,
                VideoFileCount);

            var batch =
                new List<VideoFileIndexUpsertItem>(
                    lastNumber - firstNumber);

            for (var videoNumber = firstNumber;
                 videoNumber < lastNumber;
                 videoNumber++)
            {
                batch.Add(
                    CreateVideoFile(videoNumber));
            }

            await _videoRepository.UpsertBatchAsync(
                batch,
                cancellationToken);

            if (lastNumber % ProgressInterval == 0 ||
                lastNumber == VideoFileCount)
            {
                Console.WriteLine(
                    $"  Добавлено видеофайлов: " +
                    $"{lastNumber:N0} из {VideoFileCount:N0}");
            }
        }
    }

    private FolderIndexUpsertItem CreateFolder(
        int folderNumber)
    {
        var name = CreateFolderName(folderNumber);
        var tokens = _normalizationService.Tokenize(name);

        return new FolderIndexUpsertItem(
            FullPath: GetFolderPath(folderNumber),
            Name: name,
            NormalizedName:
                _normalizationService.Normalize(name),
            SearchTokens: string.Join(' ', tokens),
            SearchStems:
                _stemService.CreateStemText(tokens),
            ParentFullPath: null,
            RootSourceId: GetRootSourceId(folderNumber),
            IsAvailable: true,
            LastSeenUtc: FixedTimestamp,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private VideoFileIndexUpsertItem CreateVideoFile(
        int videoNumber)
    {
        var folderNumber =
            GetVideoFolderNumber(videoNumber);

        var folderPath = GetFolderPath(folderNumber);
        var name = CreateVideoFileName(videoNumber);

        return new VideoFileIndexUpsertItem(
            FullPath: Path.Combine(folderPath, name),
            Name: name,
            NormalizedName:
                _normalizationService.Normalize(name),
            Extension: Path.GetExtension(name),
            SizeBytes: 1_000_000L + videoNumber,
            LastWriteTimeUtc: FixedTimestamp,
            FolderFullPath: folderPath,
            RootSourceId: GetRootSourceId(folderNumber),
            IsAvailable: true,
            LastSeenUtc: FixedTimestamp);
    }

    private static int GetVideoFolderNumber(
        int videoNumber)
    {
        var remainder = videoNumber % 5_000;

        if (remainder == 7)
        {
            return 2 * (videoNumber / 5_000);
        }

        if (remainder == 2_507)
        {
            return 2 * (videoNumber / 5_000) + 1;
        }

        return
            (videoNumber + videoNumber / 5_000) %
            FolderCount;
    }

    private static string CreateFolderName(
        int folderNumber)
    {
        return (folderNumber % 10) switch
        {
            0 => $"Фестиваль памяти {folderNumber:D5}",
            1 => $"Архив Фестиваль {folderNumber:D5}",
            2 => $"ФЕСТИВАЛЬ материал {folderNumber:D5}",
            3 => $"Съёмка фестиваль {folderNumber:D5}",
            4 => $"Лабецкая {folderNumber:D5}",
            5 => $"Почтовый архив {folderNumber:D5}",
            6 => $"Железная дорога {folderNumber:D5}",
            7 => $"Съёмка природы {folderNumber:D5}",
            8 => $"Архивная папка {folderNumber:D5}",
            _ => $"Материалы {folderNumber:D5}"
        };
    }

    private static string CreateVideoFileName(
        int videoNumber)
    {
        if (videoNumber % 5_000 == 7)
        {
            return "Фестиваль.mxf";
        }

        if (videoNumber % 5_000 == 2_507)
        {
            return "ФЕСТИВАЛЬ.mxf";
        }

        if (videoNumber % SelectiveInterval == 0)
        {
            return $"Материал Лабецкая {videoNumber:D7}.mxf";
        }

        return (videoNumber % 10) switch
        {
            0 => $"Фестиваль памяти {videoNumber:D7}.mxf",
            1 => $"ФЕСТИВАЛЬ материал {videoNumber:D7}.mxf",
            2 => $"фестиваль архив {videoNumber:D7}.mxf",
            3 => $"10_15_ФестивальПамяти_{videoNumber:D7}.mxf",
            4 => $"Съёмка природы {videoNumber:D7}.mxf",
            5 => $"Почтовый архив {videoNumber:D7}.mxf",
            6 => $"Железная дорога {videoNumber:D7}.mxf",
            7 => $"Материал природа {videoNumber:D7}.mxf",
            8 => $"Архивная съёмка {videoNumber:D7}.mxf",
            _ => $"Материалы {videoNumber:D7}.mxf"
        };
    }

    private static string GetFolderPath(
        int folderNumber)
    {
        var sourceName =
            folderNumber % 2 == 0
                ? "source-a"
                : "source-b";

        return
            $@"\\synthetic-vaf\{sourceName}\Folder_{folderNumber:D5}";
    }

    private static Guid GetRootSourceId(
        int folderNumber)
    {
        return folderNumber % 2 == 0
            ? SourceAId
            : SourceBId;
    }

    private async Task MeasureCaseAsync(
        SqliteFolderSearchPageService pageService,
        BenchmarkCase benchmarkCase,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"Сценарий: {benchmarkCase.Name}");
        Console.WriteLine(
            $"  Режим: {benchmarkCase.Mode}; " +
            $"запрос: \"{benchmarkCase.QueryText}\"; " +
            $"источники: папки " +
            $"{benchmarkCase.FolderSourceIds.Count}, видео " +
            $"{benchmarkCase.VideoSourceIds.Count}");

        var query = new FolderSearchQuery(
            benchmarkCase.QueryText,
            benchmarkCase.Mode,
            PageSize,
            benchmarkCase.FolderSourceIds,
            benchmarkCase.VideoSourceIds);

        var firstPageStopwatch = Stopwatch.StartNew();

        var firstPage = await pageService
            .SearchFirstPageAsync(query, cancellationToken);

        firstPageStopwatch.Stop();

        var releaseSession = firstPage.NextSession;

        try
        {
            var oracle = await ReadOracleAsync(
                query,
                cancellationToken);

            Console.WriteLine(
                $"  Эталон: {oracle.Ids.Length:N0} совпадений " +
                $"(папки {oracle.FolderCount:N0}, " +
                $"видео {oracle.VideoCount:N0}); " +
                $"SHA-256 ids: {oracle.Hash}");

            var traversalStopwatch = Stopwatch.StartNew();

            var traversal = await TraverseAsync(
                pageService,
                firstPage,
                oracle,
                cancellationToken);

            traversalStopwatch.Stop();

            Console.WriteLine(
                $"  Страниц: {traversal.PageCount:N0}; " +
                $"средняя: {traversal.MiddlePageIndex + 1:N0}, " +
                $"последняя: {traversal.LastPageIndex + 1:N0}; " +
                $"совпадений на последней: " +
                $"{traversal.LastPageMatchCount:N0}");

            Console.WriteLine(
                $"  Полный обход: {traversal.PageCount:N0} " +
                $"страниц; пропусков: 0; дублей: 0; " +
                $"пустых страниц: 0; SHA-256 ids: " +
                $"{traversal.ActualHash}");

            Console.WriteLine(
                $"  Полный обход (с первой порцией и COUNT, без эталона): " +
                $"{firstPageStopwatch.Elapsed.TotalMilliseconds + traversalStopwatch.Elapsed.TotalMilliseconds:N2} мс");

            Console.WriteLine(
                $"  Первый вызов поиска после служебных проверок " +
                $"(кэш ОС не сбрасывался): " +
                $"{firstPageStopwatch.Elapsed.TotalMilliseconds:N2} мс; " +
                $"совпадений: {firstPage.Matches.Count:N0}");

            var countTimings = await MeasureCountsAsync(
                query,
                oracle,
                cancellationToken);

            Console.WriteLine(
                $"  Точный COUNT: папки " +
                $"{countTimings.FolderCount:N0}, видео " +
                $"{countTimings.VideoCount:N0}, всего " +
                $"{countTimings.FolderCount + countTimings.VideoCount:N0}; " +
                $"совпал с эталоном и первой страницей");

            PrintTiming(
                "COUNT — папки",
                countTimings.FolderDurations,
                applySearchThreshold: false);

            PrintTiming(
                "COUNT — видео",
                countTimings.VideoDurations,
                applySearchThreshold: false);

            PrintTiming(
                "COUNT — папки + видео",
                countTimings.CombinedDurations,
                applySearchThreshold: false);

            var firstPageTimings =
                await MeasureFirstPageAsync(
                    pageService,
                    query,
                    oracle,
                    cancellationToken);

            PrintTiming(
                "Первая страница (включая точный COUNT)",
                firstPageTimings.Durations,
                applySearchThreshold: true,
                resultCount: firstPageTimings.ResultCount);

            var middlePageTimings =
                await MeasureNextPageAsync(
                    pageService,
                    query,
                    oracle,
                    traversal.PreMiddleSession,
                    traversal.MiddlePageIndex,
                    cancellationToken);

            PrintTiming(
                $"Средняя страница " +
                $"({traversal.MiddlePageIndex + 1:N0} из " +
                $"{traversal.PageCount:N0})",
                middlePageTimings.Durations,
                applySearchThreshold: true,
                resultCount: middlePageTimings.ResultCount);

            var lastPageTimings =
                await MeasureNextPageAsync(
                    pageService,
                    query,
                    oracle,
                    traversal.PreLastSession,
                    traversal.LastPageIndex,
                    cancellationToken);

            PrintTiming(
                $"Последняя непустая страница " +
                $"({traversal.LastPageIndex + 1:N0} из " +
                $"{traversal.PageCount:N0})",
                lastPageTimings.Durations,
                applySearchThreshold: true,
                resultCount: lastPageTimings.ResultCount);

            var validationTimings =
                await MeasureSessionValidationAsync(
                    pageService,
                    traversal.FinalSession,
                    cancellationToken);

            PrintTiming(
                "ValidateSessionAsync",
                validationTimings,
                applySearchThreshold: false);

            var pageP95Values = new[]
            {
                GetPercentile(
                    SortDurations(firstPageTimings.Durations),
                    0.95),
                GetPercentile(
                    SortDurations(middlePageTimings.Durations),
                    0.95),
                GetPercentile(
                    SortDurations(lastPageTimings.Durations),
                    0.95)
            };

            var worstP95 = pageP95Values.Max();

            Console.WriteLine(
                $"  Худший p95 страниц: {worstP95:N2} мс; " +
                $"действующий порог p95 " +
                $"{SearchP95ThresholdMilliseconds:N0} мс: " +
                $"{GetThresholdStatus(worstP95)}");
        }
        finally
        {
            pageService.ReleaseSession(releaseSession);
        }
    }

    private async Task<TraversalResult> TraverseAsync(
        SqliteFolderSearchPageService pageService,
        FolderSearchPage firstPage,
        OracleData oracle,
        CancellationToken cancellationToken)
    {
        var totalMatchCount = firstPage.TotalMatchCount;

        if (totalMatchCount != oracle.Ids.Length)
        {
            throw new InvalidOperationException(
                $"Первая страница сообщила {totalMatchCount:N0} " +
                $"совпадений, эталон — {oracle.Ids.Length:N0}.");
        }

        if (firstPage.FolderMatchCount != oracle.FolderCount ||
            firstPage.VideoMatchCount != oracle.VideoCount)
        {
            throw new InvalidOperationException(
                "Счётчики потоков первой страницы не совпали " +
                "с эталоном.");
        }

        var pageCount =
            (totalMatchCount + PageSize - 1) / PageSize;

        if (pageCount == 0)
        {
            throw new InvalidOperationException(
                "Сценарий вернул пустую выдачу; глубина страниц " +
                "не проверена.");
        }

        using var actualHash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        var position = 0;
        var pageSizes = new List<int>(pageCount);

        ValidatePage(
            firstPage,
            pageIndex: 0,
            oracle,
            ref position,
            actualHash);

        pageSizes.Add(firstPage.Matches.Count);

        var middlePageIndex = (pageCount - 1) / 2;
        var lastPageIndex = pageCount - 1;

        FolderSearchSession? preMiddleSession =
            middlePageIndex == 0
                ? null
                : firstPage.NextSession;

        FolderSearchSession? preLastSession =
            lastPageIndex == 0
                ? null
                : firstPage.NextSession;

        var currentSession = firstPage.NextSession;
        var lastPageMatchCount = firstPage.Matches.Count;

        for (var pageIndex = 1;
             pageIndex < pageCount;
             pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pageIndex == middlePageIndex)
            {
                preMiddleSession = currentSession;
            }

            if (pageIndex == lastPageIndex)
            {
                preLastSession = currentSession;
            }

            var page = await pageService.SearchNextPageAsync(
                currentSession,
                cancellationToken);

            ValidatePage(
                page,
                pageIndex,
                oracle,
                ref position,
                actualHash);

            pageSizes.Add(page.Matches.Count);
            lastPageMatchCount = page.Matches.Count;
            currentSession = page.NextSession;
        }

        if (position != oracle.Ids.Length)
        {
            throw new InvalidOperationException(
                $"Обход вернул {position:N0} совпадений, " +
                $"эталон — {oracle.Ids.Length:N0}.");
        }

        if (currentSession.HasMoreResults)
        {
            throw new InvalidOperationException(
                "После последней страницы сессия сообщает " +
                "о наличии совпадений.");
        }

        var actualHashText = Convert.ToHexString(
            actualHash.GetHashAndReset()).ToLowerInvariant();

        if (!string.Equals(
                actualHashText,
                oracle.Hash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Порядок ids полного обхода не совпал с эталоном.");
        }

        if (pageSizes.Any(size => size == 0))
        {
            throw new InvalidOperationException(
                "Полный обход содержит пустую страницу.");
        }

        return new TraversalResult(
            PageCount: pageCount,
            MiddlePageIndex: middlePageIndex,
            LastPageIndex: lastPageIndex,
            LastPageMatchCount: lastPageMatchCount,
            PreMiddleSession: preMiddleSession,
            PreLastSession: preLastSession,
            FinalSession: currentSession,
            ActualHash: actualHashText);
    }

    private static void ValidatePage(
        FolderSearchPage page,
        int pageIndex,
        OracleData oracle,
        ref int position,
        IncrementalHash? actualHash)
    {
        var expectedLength = Math.Min(
            PageSize,
            oracle.Ids.Length - pageIndex * PageSize);

        if (expectedLength <= 0)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1} находится за " +
                "пределами эталонной выдачи.");
        }

        if (page.Matches.Count != expectedLength)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1}: получено " +
                $"{page.Matches.Count:N0} совпадений, " +
                $"ожидалось {expectedLength:N0}.");
        }

        if (page.Matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1} пуста.");
        }

        if (page.TotalMatchCount != oracle.Ids.Length ||
            page.FolderMatchCount != oracle.FolderCount ||
            page.VideoMatchCount != oracle.VideoCount)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1}: счётчики не " +
                "совпали с эталоном.");
        }

        var expectedLoadedCount = Math.Min(
            oracle.Ids.Length,
            (pageIndex + 1) * PageSize);

        if (page.LoadedMatchCount != expectedLoadedCount)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1}: загружено " +
                $"{page.LoadedMatchCount:N0}, ожидалось " +
                $"{expectedLoadedCount:N0}.");
        }

        var expectedHasMore =
            (pageIndex + 1) * PageSize < oracle.Ids.Length;

        if (page.HasMoreResults != expectedHasMore)
        {
            throw new InvalidOperationException(
                $"Страница {pageIndex + 1}: HasMoreResults=" +
                $"{page.HasMoreResults}, ожидалось " +
                $"{expectedHasMore}.");
        }

        for (var index = 0;
             index < page.Matches.Count;
             index++)
        {
            var actualId = page.Matches[index].Id;
            var expectedId = oracle.Ids[position];

            if (actualId != expectedId)
            {
                throw new InvalidOperationException(
                    $"Страница {pageIndex + 1}, элемент " +
                    $"{index + 1}: id {actualId}, ожидался " +
                    $"{expectedId}.");
            }

            actualHash?.AppendData(
                GetLittleEndianBytes(actualId));

            position++;
        }
    }

    private async Task<PageTimingResult> MeasureFirstPageAsync(
        SqliteFolderSearchPageService pageService,
        FolderSearchQuery query,
        OracleData oracle,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            var page = await pageService
                .SearchFirstPageAsync(query, cancellationToken);

            try
            {
                ValidateTargetPage(
                    page,
                    pageIndex: 0,
                    oracle);
            }
            finally
            {
                pageService.ReleaseSession(page.NextSession);
            }
        }

        var durations = new List<double>(
            MeasurementIterationCount);

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var stopwatch = Stopwatch.StartNew();

            var page = await pageService
                .SearchFirstPageAsync(query, cancellationToken);

            stopwatch.Stop();

            try
            {
                ValidateTargetPage(
                    page,
                    pageIndex: 0,
                    oracle);

                durations.Add(
                    stopwatch.Elapsed.TotalMilliseconds);
            }
            finally
            {
                pageService.ReleaseSession(page.NextSession);
            }
        }

        return new PageTimingResult(
            durations,
            Math.Min(PageSize, oracle.Ids.Length));
    }

    private async Task<PageTimingResult> MeasureNextPageAsync(
        SqliteFolderSearchPageService pageService,
        FolderSearchQuery query,
        OracleData oracle,
        FolderSearchSession? prePageSession,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        if (prePageSession is null)
        {
            return await MeasureFirstPageAsync(
                pageService,
                query,
                oracle,
                cancellationToken);
        }

        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            var page = await pageService.SearchNextPageAsync(
                prePageSession,
                cancellationToken);

            ValidateTargetPage(
                page,
                pageIndex,
                oracle);
        }

        var durations = new List<double>(
            MeasurementIterationCount);

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var stopwatch = Stopwatch.StartNew();

            var page = await pageService.SearchNextPageAsync(
                prePageSession,
                cancellationToken);

            stopwatch.Stop();

            ValidateTargetPage(
                page,
                pageIndex,
                oracle);

            durations.Add(
                stopwatch.Elapsed.TotalMilliseconds);
        }

        return new PageTimingResult(
            durations,
            Math.Min(
                PageSize,
                oracle.Ids.Length - pageIndex * PageSize));
    }

    private static void ValidateTargetPage(
        FolderSearchPage page,
        int pageIndex,
        OracleData oracle)
    {
        var position = pageIndex * PageSize;

        ValidatePage(
            page,
            pageIndex,
            oracle,
            ref position,
            actualHash: null);
    }

    private async Task<double[]> MeasureSessionValidationAsync(
        SqliteFolderSearchPageService pageService,
        FolderSearchSession session,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            await pageService.ValidateSessionAsync(
                session,
                cancellationToken);
        }

        var durations = new List<double>(
            MeasurementIterationCount);

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var stopwatch = Stopwatch.StartNew();

            await pageService.ValidateSessionAsync(
                session,
                cancellationToken);

            stopwatch.Stop();

            durations.Add(
                stopwatch.Elapsed.TotalMilliseconds);
        }

        return durations.ToArray();
    }

    private async Task<CountTimingResult> MeasureCountsAsync(
        FolderSearchQuery query,
        OracleData oracle,
        CancellationToken cancellationToken)
    {
        await using var connection =
            CreateReadOnlyConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var folderCommand =
            CreateFolderCountCommand(
                connection,
                query,
                _normalizationService,
                _stemService);

        await using var videoCommand =
            CreateVideoCountCommand(
                connection,
                query,
                _normalizationService,
                _stemService);

        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            await folderCommand.ExecuteScalarAsync(
                cancellationToken);

            await videoCommand.ExecuteScalarAsync(
                cancellationToken);
        }

        var folderDurations = new List<double>(
            MeasurementIterationCount);

        var videoDurations = new List<double>(
            MeasurementIterationCount);

        var combinedDurations = new List<double>(
            MeasurementIterationCount);

        var folderCount = 0;
        var videoCount = 0;

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var combinedStopwatch = Stopwatch.StartNew();
            var folderStopwatch = Stopwatch.StartNew();

            folderCount = Convert.ToInt32(
                await folderCommand.ExecuteScalarAsync(
                    cancellationToken));

            folderStopwatch.Stop();

            var videoStopwatch = Stopwatch.StartNew();

            videoCount = Convert.ToInt32(
                await videoCommand.ExecuteScalarAsync(
                    cancellationToken));

            videoStopwatch.Stop();
            combinedStopwatch.Stop();

            folderDurations.Add(
                folderStopwatch.Elapsed.TotalMilliseconds);

            videoDurations.Add(
                videoStopwatch.Elapsed.TotalMilliseconds);

            combinedDurations.Add(
                combinedStopwatch.Elapsed.TotalMilliseconds);
        }

        if (folderCount != oracle.FolderCount ||
            videoCount != oracle.VideoCount)
        {
            throw new InvalidOperationException(
                $"Точный COUNT вернул папки {folderCount:N0}, " +
                $"видео {videoCount:N0}; эталон — " +
                $"{oracle.FolderCount:N0} и " +
                $"{oracle.VideoCount:N0}.");
        }

        return new CountTimingResult(
            FolderCount: folderCount,
            VideoCount: videoCount,
            FolderDurations: folderDurations,
            VideoDurations: videoDurations,
            CombinedDurations: combinedDurations);
    }

    private static SqliteCommand CreateFolderCountCommand(
        SqliteConnection connection,
        FolderSearchQuery query,
        ITextNormalizationService normalizationService,
        ISearchStemService stemService)
    {
        var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildFolderSearchCondition(
                command,
                normalizationService,
                stemService,
                query);

        var rootSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray();

        var sourceFilter =
            FolderSearchSql.BuildRootSourceIdFilter(
                "RootSourceId",
                rootSourceIds,
                "rootSourceId");

        command.CommandText = sourceFilter.Length == 0
            ? $"""
              SELECT COUNT(*)
              FROM Folders
              WHERE {searchCondition};
              """
            : $"""
              SELECT COUNT(*)
              FROM Folders
              WHERE {searchCondition}
                AND {sourceFilter};
              """;

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizationService.Normalize(query.Text));

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            rootSourceIds);

        return command;
    }

    private static SqliteCommand CreateVideoCountCommand(
        SqliteConnection connection,
        FolderSearchQuery query,
        ITextNormalizationService normalizationService,
        ISearchStemService stemService)
    {
        var command = connection.CreateCommand();

        var searchCondition =
            FolderSearchSql.BuildVideoSearchCondition(
                command,
                normalizationService,
                stemService,
                query);

        var rootSourceIds = query.VideoFileRootSourceIds?
            .Distinct()
            .ToArray();

        var sourceFilter =
            FolderSearchSql.BuildRootSourceIdFilter(
                "v.RootSourceId",
                rootSourceIds,
                "videoRootSourceId");

        command.CommandText = sourceFilter.Length == 0
            ? $"""
              SELECT COUNT(*)
              FROM VideoFiles v
              INNER JOIN Folders f
                  ON f.RootSourceId = v.RootSourceId
                 AND f.FullPath = v.FolderFullPath
              WHERE {searchCondition}
                AND v.IsAvailable = 1;
              """
            : $"""
              SELECT COUNT(*)
              FROM VideoFiles v
              INNER JOIN Folders f
                  ON f.RootSourceId = v.RootSourceId
                 AND f.FullPath = v.FolderFullPath
              WHERE {searchCondition}
                AND v.IsAvailable = 1
                AND {sourceFilter};
              """;

        command.Parameters.AddWithValue(
            "$normalizedQuery",
            normalizationService.Normalize(query.Text));

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            rootSourceIds,
            "videoRootSourceId");

        return command;
    }

    private async Task<OracleData> ReadOracleAsync(
        FolderSearchQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery =
            _normalizationService.Normalize(query.Text);

        var folderSourceIds = query.RootSourceIds?
            .Distinct()
            .ToArray();

        var videoSourceIds =
            query.VideoFileRootSourceIds?
                .Distinct()
                .ToArray();

        await using var connection =
            CreateReadOnlyConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        var ids = new List<long>(
            VideoFileCount);

        var folderCount = await ReadOracleStreamAsync(
            connection,
            query,
            normalizedQuery,
            folderSourceIds,
            isVideoStream: false,
            ids,
            cancellationToken);

        var videoCount = await ReadOracleStreamAsync(
            connection,
            query,
            normalizedQuery,
            videoSourceIds,
            isVideoStream: true,
            ids,
            cancellationToken);

        var idArray = ids.ToArray();

        return new OracleData(
            Ids: idArray,
            FolderCount: folderCount,
            VideoCount: videoCount,
            Hash: ComputeIdSequenceHash(idArray));
    }

    private async Task<int> ReadOracleStreamAsync(
        SqliteConnection connection,
        FolderSearchQuery query,
        string normalizedQuery,
        IReadOnlyList<Guid>? rootSourceIds,
        bool isVideoStream,
        ICollection<long> ids,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        var searchCondition = isVideoStream
            ? FolderSearchSql.BuildVideoSearchCondition(
                command,
                _normalizationService,
                _stemService,
                query)
            : FolderSearchSql.BuildFolderSearchCondition(
                command,
                _normalizationService,
                _stemService,
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
            OraclePageSize);

        command.Parameters.AddWithValue(
            "$offset",
            0);

        FolderSearchSql.AddRootSourceIdParameters(
            command,
            rootSourceIds,
            isVideoStream
                ? "videoRootSourceId"
                : "rootSourceId");

        var count = 0;

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            ids.Add(reader.GetInt64(0));
            count++;
        }

        return count;
    }

    private async Task<IndexCounts> ReadIndexCountsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            CreateReadOnlyConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        return new IndexCounts(
            FolderCount: await ReadTableCountAsync(
                connection,
                "Folders",
                cancellationToken),
            VideoFileCount: await ReadTableCountAsync(
                connection,
                "VideoFiles",
                cancellationToken));
    }

    private static async Task<long> ReadTableCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            $"SELECT COUNT(*) FROM {tableName};";

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt64(result);
    }

    private async Task<double[]> MeasureDataVersionAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            CreateReadOnlyConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText = "PRAGMA data_version;";

        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var durations = new List<double>(
            MeasurementIterationCount);

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var stopwatch = Stopwatch.StartNew();

            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            durations.Add(
                stopwatch.Elapsed.TotalMilliseconds);
        }

        return durations.ToArray();
    }

    private SqliteConnection CreateReadOnlyConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePathProvider.GetDatabasePath(),

                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = "PRAGMA busy_timeout = 5000;";

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ComputeIdSequenceHash(
        IReadOnlyList<long> ids)
    {
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        foreach (var id in ids)
        {
            hash.AppendData(
                GetLittleEndianBytes(id));
        }

        return Convert.ToHexString(
            hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] GetLittleEndianBytes(
        long value)
    {
        var bytes = new byte[sizeof(long)];

        BinaryPrimitives.WriteInt64LittleEndian(
            bytes,
            value);

        return bytes;
    }

    private static void PrintTiming(
        string name,
        IReadOnlyList<double> durations,
        bool applySearchThreshold,
        int? resultCount = null)
    {
        var sorted = SortDurations(durations);

        var p95 = GetPercentile(sorted, 0.95);

        var resultCountText = resultCount is null
            ? string.Empty
            : $"; совпадений: {resultCount.Value:N0}";

        Console.WriteLine(
            $"  {name}: min {sorted[0]:N2} мс; " +
            $"median {GetPercentile(sorted, 0.50):N2} мс; " +
            $"p95 {p95:N2} мс; " +
            $"max {sorted[^1]:N2} мс{resultCountText}");

        if (applySearchThreshold)
        {
            Console.WriteLine(
                $"    Действующий порог p95 " +
                $"{SearchP95ThresholdMilliseconds:N0} мс: " +
                $"{GetThresholdStatus(p95)}");
        }
    }

    private static string GetThresholdStatus(
        double p95Milliseconds)
    {
        if (p95Milliseconds <=
            SearchP95ThresholdMilliseconds)
        {
            return "в пределах порога";
        }

        return
            $"ПРЕВЫШЕНИЕ на " +
            $"{p95Milliseconds - SearchP95ThresholdMilliseconds:N2} мс";
    }

    private static double[] SortDurations(
        IReadOnlyList<double> durations)
    {
        var sorted = durations.ToArray();

        Array.Sort(sorted);

        return sorted;
    }

    private static double GetPercentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        var index = (int)Math.Ceiling(
            percentile * sortedValues.Count) - 1;

        index = Math.Clamp(
            index,
            0,
            sortedValues.Count - 1);

        return sortedValues[index];
    }

    private static IReadOnlyList<BenchmarkCase>
        CreateBenchmarkCases()
    {
        var folderSourceIds = new[]
        {
            SourceAId,
            SourceBId
        };

        var videoSourceIds = new[]
        {
            SourceAId,
            SourceBId
        };

        return
        [
            new(
                "Broad — смешанный scope",
                BroadQueryText,
                FolderSearchMode.Smart,
                folderSourceIds,
                videoSourceIds),

            new(
                "Selective — смешанный scope",
                SelectiveQueryText,
                FolderSearchMode.Smart,
                folderSourceIds,
                videoSourceIds)
        ];
    }

    private static void PrintEnvironmentInformation()
    {
        Console.WriteLine(
            "Video Archive Finder — синтетический бенчмарк " +
            "постраничного поиска");

        Console.WriteLine();
        Console.WriteLine(
            $"ОС: {RuntimeInformation.OSDescription}");

        Console.WriteLine(
            $".NET: {RuntimeInformation.FrameworkDescription}");

        Console.WriteLine(
            $"Логических процессоров: " +
            $"{Environment.ProcessorCount}");

        Console.WriteLine(
            $"Разрядность процесса: " +
            $"{(Environment.Is64BitProcess ? "x64" : "x86")}");

        Console.WriteLine(
            $"Методика: {BenchmarkVersion}; " +
            $"{VideoFileCount:N0} видеофайлов, " +
            $"{FolderCount:N0} папок, 2 источника, кириллица");
    }

    private sealed record BenchmarkCase(
        string Name,
        string QueryText,
        FolderSearchMode Mode,
        IReadOnlyList<Guid> FolderSourceIds,
        IReadOnlyList<Guid> VideoSourceIds);

    private sealed record OracleData(
        long[] Ids,
        int FolderCount,
        int VideoCount,
        string Hash);

    private sealed record TraversalResult(
        int PageCount,
        int MiddlePageIndex,
        int LastPageIndex,
        int LastPageMatchCount,
        FolderSearchSession? PreMiddleSession,
        FolderSearchSession? PreLastSession,
        FolderSearchSession FinalSession,
        string ActualHash);

    private sealed record PageTimingResult(
        IReadOnlyList<double> Durations,
        int ResultCount);

    private sealed record CountTimingResult(
        int FolderCount,
        int VideoCount,
        IReadOnlyList<double> FolderDurations,
        IReadOnlyList<double> VideoDurations,
        IReadOnlyList<double> CombinedDurations);

    private sealed record IndexCounts(
        long FolderCount,
        long VideoFileCount);
}
