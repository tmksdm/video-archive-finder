using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.SearchBenchmark;

internal sealed class SearchBenchmarkRunner
{
    private const int FolderCount = 100_000;
    private const int BatchSize = 1_000;
    private const int WarmupIterationCount = 3;
    private const int MeasurementIterationCount = 15;
    private const int MaximumResultCount = 200;

    private static readonly Guid RootSourceId =
        Guid.Parse("20c05f66-b40e-40d8-802b-d74c660577c4");

    private static readonly DateTimeOffset LastSeenUtc =
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

    private readonly SqliteFolderSearchService
        _searchService;

    private readonly ITextNormalizationService
        _normalizationService;

    private readonly ISearchStemService
        _stemService;

    public SearchBenchmarkRunner(
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

        _normalizationService =
            new TextNormalizationService();

        _stemService =
            new RussianSearchStemService();

        _folderRepository =
            new SqliteFolderIndexRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        _searchService =
            new SqliteFolderSearchService(
                _databasePathProvider,
                _normalizationService,
                _stemService,
                NullLogger<
                    SqliteFolderSearchService>.Instance);
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

        Console.WriteLine();
        Console.WriteLine(
            $"Создан синтетический индекс: {FolderCount:N0} папок");

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
            $"Прогревов для каждого запроса: " +
            $"{WarmupIterationCount}");

        Console.WriteLine(
            $"Измерений для каждого запроса: " +
            $"{MeasurementIterationCount}");

        Console.WriteLine();

        foreach (var benchmarkCase in CreateBenchmarkCases())
        {
            await MeasureCaseAsync(
                benchmarkCase,
                cancellationToken);
        }
    }

    private async Task PopulateIndexAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Заполнение временного синтетического индекса...");

        for (var firstNumber = 1;
             firstNumber <= FolderCount;
             firstNumber += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastNumber = Math.Min(
                firstNumber + BatchSize - 1,
                FolderCount);

            var batch =
                new List<FolderIndexUpsertItem>(
                    lastNumber - firstNumber + 1);

            for (var folderNumber = firstNumber;
                 folderNumber <= lastNumber;
                 folderNumber++)
            {
                batch.Add(
                    CreateFolder(folderNumber));
            }

            await _folderRepository.UpsertBatchAsync(
                batch,
                cancellationToken);

            if (lastNumber % 10_000 == 0 ||
                lastNumber == FolderCount)
            {
                Console.WriteLine(
                    $"  Добавлено: {lastNumber:N0} " +
                    $"из {FolderCount:N0}");
            }
        }
    }

    private FolderIndexUpsertItem CreateFolder(
        int folderNumber)
    {
        var name = CreateFolderName(folderNumber);

        var tokens =
            _normalizationService.Tokenize(name);

        return new FolderIndexUpsertItem(
            FullPath:
                $@"X:\SyntheticVideoArchive\" +
                $"Folder_{folderNumber:D6}",
            Name: name,
            NormalizedName:
                _normalizationService.Normalize(name),
            SearchTokens:
                string.Join(' ', tokens),
            SearchStems:
                _stemService.CreateStemText(tokens),
            ParentFullPath: null,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            LastSeenUtc: LastSeenUtc,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static string CreateFolderName(
        int folderNumber)
    {
        if (folderNumber % 1_000 == 0)
        {
            return
                $"Контрольная папка {folderNumber:D6}";
        }

        if (folderNumber % 40 == 0)
        {
            return
                $"Машинный репортаж {folderNumber:D6}";
        }

        if (folderNumber % 25 == 0)
        {
            return
                $"Железная дорога {folderNumber:D6}";
        }

        if (folderNumber % 10 == 0)
        {
            return
                $"Почтовый архив {folderNumber:D6}";
        }

        return
            $"Архивная съемка материал " +
            $"{folderNumber:D6}";
    }

    private async Task MeasureCaseAsync(
        BenchmarkCase benchmarkCase,
        CancellationToken cancellationToken)
    {
        var query =
            new FolderSearchQuery(
                benchmarkCase.QueryText,
                benchmarkCase.Mode,
                MaximumResultCount);

        for (var iteration = 0;
             iteration < WarmupIterationCount;
             iteration++)
        {
            await _searchService.SearchAsync(
                query,
                cancellationToken);
        }

        var durations = new List<double>(
            MeasurementIterationCount);

        var resultCount = 0;

        for (var iteration = 0;
             iteration < MeasurementIterationCount;
             iteration++)
        {
            var startedAt = Stopwatch.GetTimestamp();

            var results =
                await _searchService.SearchAsync(
                    query,
                    cancellationToken);

            var elapsed =
                Stopwatch.GetElapsedTime(startedAt);

            durations.Add(
                elapsed.TotalMilliseconds);

            resultCount = results.Count;
        }

        durations.Sort();

        Console.WriteLine(benchmarkCase.Name);

        Console.WriteLine(
            $"  Режим: {benchmarkCase.Mode}; " +
            $"запрос: \"{benchmarkCase.QueryText}\"");

        Console.WriteLine(
            $"  Результатов: {resultCount}");

        Console.WriteLine(
            $"  min: {durations[0]:N2} мс; " +
            $"median: {GetPercentile(durations, 0.50):N2} мс; " +
            $"p95: {GetPercentile(durations, 0.95):N2} мс; " +
            $"max: {durations[^1]:N2} мс");

        Console.WriteLine();
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
        return
        [
            new(
                "Exact — частое совпадение",
                "архив",
                FolderSearchMode.Exact),

            new(
                "Exact — редкое совпадение",
                "контрольная папка 050000",
                FolderSearchMode.Exact),

            new(
                "Exact — совпадений нет",
                "несуществующий запрос",
                FolderSearchMode.Exact),

            new(
                "Smart — словоформа «почта»",
                "почта",
                FolderSearchMode.Smart),

            new(
                "Smart — словоформа «дорога»",
                "дорога",
                FolderSearchMode.Smart),

            new(
                "Smart — два частых токена",
                "архив материал",
                FolderSearchMode.Smart)
        ];
    }

    private static void PrintEnvironmentInformation()
    {
        Console.WriteLine(
            "Video Archive Finder — измерение SQLite-поиска");

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
    }

    private sealed record BenchmarkCase(
        string Name,
        string QueryText,
        FolderSearchMode Mode);
}
