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
    private readonly IWindowsShellService _windowsShellService;
    private readonly IClipboardService _clipboardService;
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
    private bool _isLoadingSources;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUncPathCommand))]
    private bool _isAddingSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUncPathCommand))]
    private bool _isRemovingSource;


    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _statusText = "Загрузка источников архива...";

    public MainWindowViewModel(
        IArchiveSourceService archiveSourceService,
        IWindowsShellService windowsShellService,
        IClipboardService clipboardService,
        IArchiveSourceAvailabilityChecker archiveSourceAvailabilityChecker,
        ILocalFolderPicker localFolderPicker,
        IUncPathInputDialog uncPathInputDialog,
        IArchiveSourceRemovalConfirmationDialog
            archiveSourceRemovalConfirmationDialog,
        ILogger<MainWindowViewModel> logger)
    {
        _archiveSourceService = archiveSourceService;
        _windowsShellService = windowsShellService;
        _clipboardService = clipboardService;
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

    [RelayCommand(CanExecute = nameof(CanOpenArchiveSource))]
    private void OpenArchiveSource(
        ArchiveSourceItemViewModel? source)
    {
        if (!CanOpenArchiveSource(source))
        {
            StatusText = "Источник сейчас недоступен";
            return;
        }

        try
        {
            _windowsShellService.OpenFolder(source!.FullPath);

            StatusText =
                $"Источник «{source.DisplayName}» открыт в Проводнике";

            _logger.LogInformation(
                "Opened archive source {SourceId} in Windows Explorer.",
                source.Id);
        }
        catch (Exception exception)
        {
            StatusText =
                $"Не удалось открыть источник «{source!.DisplayName}»";

            _logger.LogError(
                exception,
                "Could not open archive source {SourceId} in Windows Explorer.",
                source.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyArchiveSourcePath))]
    private void CopyArchiveSourcePath(
        ArchiveSourceItemViewModel? source)
    {
        if (!CanCopyArchiveSourcePath(source))
        {
            StatusText = "Не удалось определить путь источника";
            return;
        }

        try
        {
            _clipboardService.SetText(source!.FullPath);

            StatusText =
                $"Путь источника «{source.DisplayName}» скопирован";

            _logger.LogInformation(
                "Copied archive source {SourceId} path to the clipboard.",
                source.Id);
        }
        catch (Exception exception)
        {
            StatusText =
                $"Не удалось скопировать путь источника «{source!.DisplayName}»";

            _logger.LogError(
                exception,
                "Could not copy archive source {SourceId} path to the clipboard.",
                source.Id);
        }
    }

    private static bool CanCopyArchiveSourcePath(
        ArchiveSourceItemViewModel? source)
    {
        return !string.IsNullOrWhiteSpace(source?.FullPath);
    }

    private static bool CanOpenArchiveSource(
        ArchiveSourceItemViewModel? source)
    {
        return source?.Availability ==
               ArchiveSourceAvailability.Available;
    }

    private bool CanAddSource()
    {
        return !IsLoadingSources &&
               !IsAddingSource &&
               !IsRemovingSource;
    }

    public async Task RemoveSourcesAsync(
        IReadOnlyCollection<ArchiveSourceItemViewModel> selectedSources)
    {
        ArgumentNullException.ThrowIfNull(selectedSources);

        if (selectedSources.Count == 0 ||
            IsLoadingSources ||
            IsAddingSource ||
            IsRemovingSource)
        {
            return;
        }

        var sourcesToRemove = selectedSources
            .Where(selectedSource =>
                Sources.Any(source => source.Id == selectedSource.Id))
            .DistinctBy(source => source.Id)
            .ToList();

        if (sourcesToRemove.Count == 0)
        {
            return;
        }

        var singleSource = sourcesToRemove.Count == 1
            ? sourcesToRemove[0]
            : null;

        var removalConfirmed =
            _archiveSourceRemovalConfirmationDialog.ConfirmRemoval(
                sourcesToRemove.Count,
                singleSource?.DisplayName,
                singleSource?.FullPath);

        if (!removalConfirmed)
        {
            StatusText = sourcesToRemove.Count == 1
                ? "Удаление источника отменено"
                : "Удаление выбранных источников отменено";

            return;
        }

        IsRemovingSource = true;

        StatusText = sourcesToRemove.Count == 1
            ? $"Удаление источника «{singleSource!.DisplayName}» из приложения..."
            : $"Удаление источников из приложения: {sourcesToRemove.Count}...";

        try
        {
            var sourceIds = sourcesToRemove
                .Select(source => source.Id)
                .ToArray();

            var wereRemoved =
                await _archiveSourceService.RemoveManyAsync(sourceIds);

            if (!wereRemoved)
            {
                StatusText =
                    "Не удалось удалить источники: список источников изменился";

                _logger.LogWarning(
                    "One or more archive sources were not found during batch removal.");

                return;
            }

            foreach (var source in sourcesToRemove)
            {
                Sources.Remove(source);
            }

            HasSources = Sources.Count > 0;

            StatusText = sourcesToRemove.Count == 1
                ? HasSources
                    ? $"Источник «{singleSource!.DisplayName}» удалён только из приложения"
                    : "Источник удалён только из приложения. " +
                      "Папки и файлы на диске не изменены."
                : HasSources
                    ? $"Источники удалены только из приложения: {sourcesToRemove.Count}"
                    : $"Источники удалены только из приложения: {sourcesToRemove.Count}. " +
                      "Папки и файлы на диске не изменены.";

            _logger.LogInformation(
                "Removed {SourceCount} archive sources from the application.",
                sourcesToRemove.Count);
        }
        catch (Exception exception)
        {
            StatusText = sourcesToRemove.Count == 1
                ? "Не удалось удалить источник из приложения"
                : "Не удалось удалить выбранные источники из приложения";

            _logger.LogError(
                exception,
                "Could not remove {SourceCount} archive sources.",
                sourcesToRemove.Count);
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

        OpenArchiveSourceCommand.NotifyCanExecuteChanged();

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
        finally
        {
            OpenArchiveSourceCommand.NotifyCanExecuteChanged();
        }
    }

}
