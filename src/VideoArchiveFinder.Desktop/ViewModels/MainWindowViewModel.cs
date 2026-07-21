using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ArchiveSources;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveSourceService _archiveSourceService;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoadingSources;

    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _statusText = "Загрузка источников архива...";

    public MainWindowViewModel(
        IArchiveSourceService archiveSourceService,
        ILogger<MainWindowViewModel> logger)
    {
        _archiveSourceService = archiveSourceService;
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
}
