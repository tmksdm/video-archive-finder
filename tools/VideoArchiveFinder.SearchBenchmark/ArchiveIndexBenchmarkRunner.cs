using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Domain.ArchiveSources;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;

namespace VideoArchiveFinder.SearchBenchmark;

internal sealed class ArchiveIndexBenchmarkRunner
{
    private const int ProgressInterval = 500;

    private readonly TemporaryApplicationDataDirectoryProvider
        _dataDirectoryProvider;

    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    public ArchiveIndexBenchmarkRunner(
        TemporaryApplicationDataDirectoryProvider
            dataDirectoryProvider)
    {
        _dataDirectoryProvider =
            dataDirectoryProvider;

        _databasePathProvider =
            new IndexDatabasePathProvider(
                _dataDirectoryProvider);
    }

    public async Task RunAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException(
                "Archive path cannot be empty.",
                nameof(archivePath));
        }

        if (!Directory.Exists(archivePath))
        {
            throw new DirectoryNotFoundException(
                $"Архив недоступен: {archivePath}");
        }

        var databaseInitializer =
            new SqliteIndexDatabaseInitializer(
                _databasePathProvider,
                NullLogger<
                    SqliteIndexDatabaseInitializer>.Instance);

        var folderRepository =
            new SqliteFolderIndexRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteFolderIndexRepository>.Instance);

        var videoFileRepository =
            new SqliteVideoFileIndexRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteVideoFileIndexRepository>.Instance);

        var folderIndexingStateRepository =
            new SqliteFolderIndexingStateRepository(
                _databasePathProvider,
                NullLogger<
                    SqliteFolderIndexingStateRepository>.Instance);

        var normalizationService =
            new TextNormalizationService();

        var indexingService =
            new FolderIndexingService(
                new SystemFolderTreeEnumerator(
                    new SystemFolderFileSystem()),
                folderRepository,
                new VideoFileDiscoveryService(
                    new SystemVideoFileSystem(),
                    new VideoFileCandidatePolicy(),
                    NullLogger<
                        VideoFileDiscoveryService>.Instance),
                videoFileRepository,
                new NoOpVideoFileAnalysisQueue(),
                folderIndexingStateRepository,
                normalizationService,
                new RussianSearchStemService(),
                NullLogger<
                    FolderIndexingService>.Instance);

        await databaseInitializer.InitializeAsync(
            cancellationToken);

        Console.WriteLine(
            "Video Archive Finder — измерение реального архива");

        Console.WriteLine();
        Console.WriteLine($"Архив: {archivePath}");
        Console.WriteLine(
            "FFprobe и создание миниатюр: отключены");

        var lastReportedFolderCount = 0;
        var stopwatch = Stopwatch.StartNew();

        var progress =
            new InlineProgress<FolderIndexingProgress>(
                value =>
                {
                    if (value.Stage !=
                            FolderIndexingStage.Completed &&
                        value.DiscoveredFolderCount -
                            lastReportedFolderCount <
                            ProgressInterval)
                    {
                        return;
                    }

                    lastReportedFolderCount =
                        value.DiscoveredFolderCount;

                    Console.WriteLine(
                        $"  Папок просмотрено: " +
                        $"{value.DiscoveredFolderCount:N0}; " +
                        $"записано: " +
                        $"{value.IndexedFolderCount:N0}; " +
                        $"ошибок: {value.ErrorCount:N0}; " +
                        $"время: " +
                        $"{stopwatch.Elapsed.TotalSeconds:N1} с");
                });

        using var memoryMonitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var memoryMonitor =
            MonitorPeakWorkingSetAsync(
                memoryMonitorCancellation.Token);

        FolderIndexingResult result;

        try
        {
            result = await indexingService.ScanAsync(
                ArchiveSource.Create(archivePath),
                progress,
                cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            memoryMonitorCancellation.Cancel();
        }

        var peakWorkingSetBytes =
            await memoryMonitor;

        var databasePath =
            _databasePathProvider.GetDatabasePath();

        var counts =
            await ReadIndexCountsAsync(
                databasePath,
                cancellationToken);

        var databaseSizeBytes =
            new FileInfo(databasePath).Length;

        Console.WriteLine();
        Console.WriteLine("Результат:");
        Console.WriteLine(
            $"  Папок обнаружено: " +
            $"{result.DiscoveredFolderCount:N0}");

        Console.WriteLine(
            $"  Папок в индексе: {counts.FolderCount:N0}");

        Console.WriteLine(
            $"  Видеофайлов в индексе: " +
            $"{counts.VideoFileCount:N0}");

        Console.WriteLine(
            $"  Ошибок: {result.ErrorCount:N0}");

        Console.WriteLine(
            $"  Время: {stopwatch.Elapsed.TotalSeconds:N2} с");

        Console.WriteLine(
            $"  Пиковая рабочая память: " +
            $"{peakWorkingSetBytes / 1024d / 1024d:N1} МБ");

        Console.WriteLine(
            $"  Размер SQLite-базы: " +
            $"{databaseSizeBytes / 1024d / 1024d:N2} МБ");
    }

    private static async Task<long>
        MonitorPeakWorkingSetAsync(
            CancellationToken cancellationToken)
    {
        using var process =
            Process.GetCurrentProcess();

        var peakWorkingSetBytes = 0L;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                process.Refresh();

                peakWorkingSetBytes = Math.Max(
                    peakWorkingSetBytes,
                    process.WorkingSet64);

                await Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            process.Refresh();

            return Math.Max(
                peakWorkingSetBytes,
                process.WorkingSet64);
        }
    }

    private static async Task<IndexCounts>
        ReadIndexCountsAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

        await using var connection =
            new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        var folderCount =
            await ReadCountAsync(
                connection,
                "Folders",
                cancellationToken);

        var videoFileCount =
            await ReadCountAsync(
                connection,
                "VideoFiles",
                cancellationToken);

        return new IndexCounts(
            folderCount,
            videoFileCount);
    }

    private static async Task<long> ReadCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            $"SELECT COUNT(*) FROM {tableName};";

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt64(value);
    }

    private sealed class NoOpVideoFileAnalysisQueue
        : IVideoFileAnalysisQueue
    {
        public ValueTask EnqueueAsync(
            VideoFileAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class InlineProgress<T>
        : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }

    private sealed record IndexCounts(
        long FolderCount,
        long VideoFileCount);
}
