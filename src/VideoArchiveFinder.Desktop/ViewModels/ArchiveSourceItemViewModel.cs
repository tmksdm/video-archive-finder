using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.ViewModels;

public sealed partial class ArchiveSourceItemViewModel :
    ObservableObject
{

    private int _previousDiscoveredFolderCount;
    private int _previousIndexedFolderCount;
    private int _previousIndexingErrorCount;
    private DateTimeOffset? _previousLastIndexedAtUtc;

    public ArchiveSourceItemViewModel(ArchiveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        Id = source.Id;
        DisplayName = source.DisplayName;
        FullPath = source.FullPath;
        SourceType = source.SourceType;
    }

    public ArchiveSource Source { get; }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string FullPath { get; }

    public ArchiveSourceType SourceType { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    [NotifyPropertyChangedFor(nameof(AvailabilityColor))]
    [NotifyPropertyChangedFor(nameof(AvailabilityToolTip))]
    private ArchiveSourceAvailability _availability =
        ArchiveSourceAvailability.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexingSummaryText))]
    [NotifyPropertyChangedFor(nameof(HasIndexingDetails))]
    private bool _isIndexing;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexingSummaryText))]
    private int _discoveredFolderCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexingSummaryText))]
    private int _indexedFolderCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexingSummaryText))]
    private int _indexingErrorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentIndexingPath))]
    private string? _currentIndexingPath;

    [ObservableProperty]
    private string _indexingStatusText =
        "Индекс папок ещё не создан";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastIndexingText))]
    [NotifyPropertyChangedFor(nameof(IndexingSummaryText))]
    [NotifyPropertyChangedFor(nameof(HasIndexingDetails))]
    private DateTimeOffset? _lastIndexedAtUtc;


    public bool HasCurrentIndexingPath =>
        !string.IsNullOrWhiteSpace(CurrentIndexingPath);

    public bool HasIndexingDetails =>
        IsIndexing || LastIndexedAtUtc.HasValue;

    public string IndexingSummaryText
    {
        get
        {
            if (IsIndexing)
            {
                return
                    $"Найдено: {DiscoveredFolderCount}  •  " +
                    $"записано: {IndexedFolderCount}  •  " +
                    $"ошибок: {IndexingErrorCount}";
            }

            if (LastIndexedAtUtc.HasValue)
            {
                return
                    $"{IndexedFolderCount} папок  •  " +
                    $"ошибок: {IndexingErrorCount}  •  " +
                    $"{LastIndexedAtUtc.Value.ToLocalTime():g}";
            }

            return string.Empty;
        }
    }


    public string LastIndexingText =>
        LastIndexedAtUtc.HasValue
            ? $"Последнее индексирование: " +
              $"{LastIndexedAtUtc.Value.ToLocalTime():g}"
            : "Ранее не индексировался";

    public string SourceTypeText => SourceType switch
    {
        ArchiveSourceType.LocalFolder => "Локальная папка",
        ArchiveSourceType.UncPath => "Сетевой UNC-путь",
        _ => "Неизвестный источник"
    };

    public string SourceTypeShortText => SourceType switch
    {
        ArchiveSourceType.LocalFolder => "Локальный",
        ArchiveSourceType.UncPath => "UNC",
        _ => "Неизвестный"
    };

    public string AvailabilityText => Availability switch
    {
        ArchiveSourceAvailability.Checking => "Проверка...",
        ArchiveSourceAvailability.Available => "Доступен",
        ArchiveSourceAvailability.Unavailable => "Недоступен",
        ArchiveSourceAvailability.TimedOut => "Не отвечает",
        _ => "Не проверено"
    };

    public string AvailabilityColor => Availability switch
    {
        ArchiveSourceAvailability.Checking => "#2563EB",
        ArchiveSourceAvailability.Available => "#15803D",
        ArchiveSourceAvailability.Unavailable => "#B91C1C",
        ArchiveSourceAvailability.TimedOut => "#B45309",
        _ => "#6B7280"
    };

    public string AvailabilityToolTip => Availability switch
    {
        ArchiveSourceAvailability.Checking =>
            "Выполняется проверка доступности источника",
        ArchiveSourceAvailability.Available =>
            "Путь доступен",
        ArchiveSourceAvailability.Unavailable =>
            "Папка не найдена или доступ к ней невозможен",
        ArchiveSourceAvailability.TimedOut =>
            "Источник не ответил за отведённое время",
        _ =>
            "Доступность источника ещё не проверялась"
    };

    public void RestoreIndexingState(
        FolderIndexingState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.RootSourceId != Id)
        {
            throw new ArgumentException(
                "Indexing state belongs to another archive source.",
                nameof(state));
        }

        DiscoveredFolderCount =
            state.DiscoveredFolderCount;

        IndexedFolderCount =
            state.IndexedFolderCount;

        IndexingErrorCount =
            state.ErrorCount;

        LastIndexedAtUtc =
            state.CompletedAtUtc;

        CurrentIndexingPath = null;
        IsIndexing = false;

        IndexingStatusText = state.ErrorCount == 0
            ? "Индексирование завершено"
            : "Индексирование завершено с ошибками";
    }

    public void BeginIndexing()
    {
        _previousDiscoveredFolderCount =
            DiscoveredFolderCount;

        _previousIndexedFolderCount =
            IndexedFolderCount;

        _previousIndexingErrorCount =
            IndexingErrorCount;

        _previousLastIndexedAtUtc =
            LastIndexedAtUtc;

        IsIndexing = true;
        DiscoveredFolderCount = 0;
        IndexedFolderCount = 0;
        IndexingErrorCount = 0;
        CurrentIndexingPath = FullPath;
        IndexingStatusText = "Индексирование папок...";
    }

    public void ApplyIndexingProgress(
        FolderIndexingProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        DiscoveredFolderCount =
            progress.DiscoveredFolderCount;

        IndexedFolderCount =
            progress.IndexedFolderCount;

        IndexingErrorCount =
            progress.ErrorCount;

        CurrentIndexingPath =
            progress.CurrentPath;

        IndexingStatusText = progress.Stage switch
        {
            FolderIndexingStage.Enumerating =>
                "Поиск папок...",
            FolderIndexingStage.WritingBatch =>
                "Сохранение папок в индекс...",
            FolderIndexingStage.Completed =>
                "Завершение индексирования...",
            _ =>
                "Индексирование папок..."
        };
    }

    public void CompleteIndexing(
        FolderIndexingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        DiscoveredFolderCount =
            result.DiscoveredFolderCount;

        IndexedFolderCount =
            result.IndexedFolderCount;

        IndexingErrorCount =
            result.ErrorCount;

        LastIndexedAtUtc =
            result.CompletedAtUtc;

        CurrentIndexingPath = null;
        IsIndexing = false;

        IndexingStatusText = result.ErrorCount == 0
            ? "Индексирование завершено"
            : "Индексирование завершено с ошибками";
    }

    public void MarkIndexingCancelled()
    {
        RestorePreviousIndexingStatistics();
        CurrentIndexingPath = null;
        IsIndexing = false;
        IndexingStatusText = "Индексирование отменено";
    }


    public void MarkIndexingFailed()
    {
        RestorePreviousIndexingStatistics();
        CurrentIndexingPath = null;
        IsIndexing = false;
        IndexingStatusText = "Ошибка индексирования";
    }

    private void RestorePreviousIndexingStatistics()
    {
        DiscoveredFolderCount =
            _previousDiscoveredFolderCount;

        IndexedFolderCount =
            _previousIndexedFolderCount;

        IndexingErrorCount =
            _previousIndexingErrorCount;

        LastIndexedAtUtc =
            _previousLastIndexedAtUtc;
    }


}
