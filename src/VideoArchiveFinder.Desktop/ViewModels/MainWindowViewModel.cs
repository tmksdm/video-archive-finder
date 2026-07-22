using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Desktop.Services;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveSourceService _archiveSourceService;
    private readonly ILocalFolderPicker _localFolderPicker;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    private bool _isLoadingSources;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLocalFolderCommand))]
    private bool _isAddingSource;

    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _statusText = "Загрузка источников архива...";

    public MainWindowViewModel(
        IArchiveSourceService archiveSourceService,
        ILocalFolderPicker localFolderPicker,
        ILogger<MainWindowViewModel> logger)
    {
        _archiveSourceService = archiveSourceService;
        _localFolderPicker = localFolderPicker;
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
                Sources.Add(new ArchiveSourceItemViewModel(source));
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

    [RelayCommand(CanExecute = nameof(CanAddLocalFolder))]
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

            StatusText = "Добавление источника архива...";

            var result = await _archiveSourceService.AddAsync(
                selectedPath);

            if (!result.WasAdded)
            {
                StatusText =
                    $"Источник «{result.Source.DisplayName}» уже добавлен";

                return;
            }

            InsertSourceInDisplayOrder(
                new ArchiveSourceItemViewModel(result.Source));

            HasSources = true;
            StatusText =
                $"Источник «{result.Source.DisplayName}» добавлен";

            _logger.LogInformation(
                "Archive source {SourceId} was added from {SourcePath}.",
                result.Source.Id,
                result.Source.FullPath);
        }
        catch (Exception exception)
        {
            StatusText = "Не удалось добавить источник архива";

            _logger.LogError(
                exception,
                "Archive source could not be added.");
        }
        finally
        {
            IsAddingSource = false;
        }
    }

    private bool CanAddLocalFolder()
    {
        return !IsLoadingSources &&
               !IsAddingSource;
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
}
