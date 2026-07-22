using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Desktop.Services;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveSourceService _archiveSourceService;
    private readonly IArchiveSourceAvailabilityChecker
        _archiveSourceAvailabilityChecker;
    private readonly ILocalFolderPicker _localFolderPicker;
    private readonly IUncPathInputDialog _uncPathInputDialog;
    private readonly IArchiveSourceRemovalConfirmationDialog
        _archiveSourceRemovalConfirmationDialog;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUncPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private bool _isLoadingSources;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUncPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private bool _isAddingSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUncPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private bool _isRemovingSource;


    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _statusText = "Загрузка источников архива...";

    public MainWindowViewModel(
        IArchiveSourceService archiveSourceService,
        IArchiveSourceAvailabilityChecker archiveSourceAvailabilityChecker,
        ILocalFolderPicker localFolderPicker,
        IUncPathInputDialog uncPathInputDialog,
        IArchiveSourceRemovalConfirmationDialog
            archiveSourceRemovalConfirmationDialog,
        ILogger<MainWindowViewModel> logger)
    {
        _archiveSourceService = archiveSourceService;
        _archiveSourceAvailabilityChecker =
            archiveSourceAvailabilityChecker;
        _localFolderPicker = localFolderPicker;
        _uncPathInputDialog = uncPathInputDialog;
        _archiveSourceRemovalConfirmationDialog =
            archiveSourceRemovalConfirmationDialog;
        _logger = logger;
    }


    public ObservableCollection<ArchiveSourceItemViewModel> Sources { get; } = [];

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsLoadingSources)
        {
            return;
        }

        IsLoadingSources = true;
        StatusText = "Загрузка источников архива...";

        try
        {
            var savedSources = await _archiveSourceService.GetAllAsync(
                cancellationToken);

            Sources.Clear();

            foreach (var source in savedSources.OrderBy(
                source => source.DisplayName,
                StringComparer.CurrentCultureIgnoreCase))
            {
                Sources.Add(CreateSourceItem(source));
            }

            HasSources = Sources.Count > 0;
            StatusText = HasSources
                ? $"Источников архива: {Sources.Count}"
                : "Источники архива ещё не добавлены";

            _logger.LogInformation(
                "Loaded {SourceCount} archive sources.",
                Sources.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Загрузка источников отменена";
            throw;
        }
        catch (Exception exception)
        {
            Sources.Clear();
            HasSources = false;
            StatusText = "Не удалось загрузить источники архива";

            _logger.LogError(
                exception,
                "Archive sources could not be loaded.");
        }
        finally
        {
            IsLoadingSources = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSource))]
    private async Task AddLocalFolderAsync()
    {
        IsAddingSource = true;

        try
        {
            var selectedPath = _localFolderPicker.PickFolder();

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            await AddSourceAsync(selectedPath);
        }
        catch (Exception exception)
        {
            HandleAddSourceError(
                exception,
                "Local archive source could not be added.");
        }
        finally
        {
            IsAddingSource = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSource))]
    private async Task AddUncPathAsync()
    {
        IsAddingSource = true;

        try
        {
            var enteredPath = _uncPathInputDialog.ShowDialog();

            if (string.IsNullOrWhiteSpace(enteredPath))
            {
                return;
            }

            await AddSourceAsync(enteredPath);
        }
        catch (Exception exception)
        {
            HandleAddSourceError(
                exception,
                "UNC archive source could not be added.");
        }
        finally
        {
            IsAddingSource = false;
        }
    }

    private bool CanAddSource()
    {
        return !IsLoadingSources &&
               !IsAddingSource &&
               !IsRemovingSource;
    }

    private bool CanRemoveSource(
        ArchiveSourceItemViewModel? source)
    {
        return source is not null &&
               !IsLoadingSources &&
               !IsAddingSource &&
               !IsRemovingSource;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSource))]
    private async Task RemoveSourceAsync(
        ArchiveSourceItemViewModel? source)
    {
        if (source is null)
        {
            return;
        }

        var removalConfirmed =
            _archiveSourceRemovalConfirmationDialog.ConfirmRemoval(
                source.DisplayName,
                source.FullPath);

        if (!removalConfirmed)
        {
            StatusText = "Удаление источника отменено";
            return;
        }

        IsRemovingSource = true;
        StatusText =
            $"Удаление источника «{source.DisplayName}» из приложения...";

        try
        {
            var wasRemoved =
                await _archiveSourceService.RemoveAsync(source.Id);

            if (!wasRemoved)
            {
                StatusText =
                    "Источник уже отсутствует в настройках приложения";

                _logger.LogWarning(
                    "Archive source {SourceId} was not found during removal.",
                    source.Id);

                return;
            }

            Sources.Remove(source);
            HasSources = Sources.Count > 0;

            StatusText = HasSources
                ? $"Источник «{source.DisplayName}» удалён только из приложения"
                : "Источник удалён только из приложения. " +
                  "Папки и файлы на диске не изменены.";

            _logger.LogInformation(
                "Archive source {SourceId} was removed from the application.",
                source.Id);
        }
        catch (Exception exception)
        {
            StatusText =
                "Не удалось удалить источник из приложения";

            _logger.LogError(
                exception,
                "Archive source {SourceId} could not be removed.",
                source.Id);
        }
        finally
        {
            IsRemovingSource = false;
        }
    }


    private async Task AddSourceAsync(string fullPath)
    {
        StatusText = "Добавление источника архива...";

        var result = await _archiveSourceService.AddAsync(fullPath);

        if (!result.WasAdded)
        {
            StatusText =
                $"Источник «{result.Source.DisplayName}» уже добавлен";

            return;
        }

        InsertSourceInDisplayOrder(
            CreateSourceItem(result.Source));

        HasSources = true;
        StatusText =
            $"Источник «{result.Source.DisplayName}» добавлен";

        _logger.LogInformation(
            "Archive source {SourceId} was added from {SourcePath}.",
            result.Source.Id,
            result.Source.FullPath);
    }

    private void HandleAddSourceError(
        Exception exception,
        string logMessage)
    {
        StatusText = "Не удалось добавить источник архива";

        _logger.LogError(
            exception,
            logMessage);
    }

    private void InsertSourceInDisplayOrder(
        ArchiveSourceItemViewModel newSource)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var insertionIndex = 0;

        while (insertionIndex < Sources.Count &&
               comparer.Compare(
                   Sources[insertionIndex].DisplayName,
                   newSource.DisplayName) <= 0)
        {
            insertionIndex++;
        }

        Sources.Insert(
            insertionIndex,
            newSource);
    }
    private ArchiveSourceItemViewModel CreateSourceItem(
        ArchiveSource source)
    {
        var sourceItem = new ArchiveSourceItemViewModel(source);

        _ = CheckSourceAvailabilityAsync(sourceItem);

        return sourceItem;
    }

    private async Task CheckSourceAvailabilityAsync(
        ArchiveSourceItemViewModel sourceItem)
    {
        sourceItem.Availability =
            ArchiveSourceAvailability.Checking;

        try
        {
            sourceItem.Availability =
                await _archiveSourceAvailabilityChecker.CheckAsync(
                    sourceItem.FullPath);
        }
        catch (Exception exception)
        {
            sourceItem.Availability =
                ArchiveSourceAvailability.Unavailable;

            _logger.LogWarning(
                exception,
                "Failed to check availability of archive source {SourceId}.",
                sourceItem.Id);
        }
    }
}
