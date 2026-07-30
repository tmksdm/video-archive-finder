using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class FolderSearchViewModel
    : ObservableObject, IDisposable
{
    private const int DebounceMilliseconds = 250;
    private const int MaximumDisplayedResults = 200;

    private readonly IFolderSearchService
        _folderSearchService;

    private readonly IFolderSearchContextProvider
        _folderSearchContextProvider;

    private readonly FolderSearchTreeBuilder
        _folderSearchTreeBuilder;

    private readonly ILogger<FolderSearchViewModel>
        _logger;

    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private bool _isDisposed;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private FolderSearchMode _searchMode =
        FolderSearchMode.Smart;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _resultsSummary =
        "Введите запрос для поиска по папкам";

    public FolderSearchViewModel(
        IFolderSearchService folderSearchService,
        IFolderSearchContextProvider
            folderSearchContextProvider,
        FolderSearchTreeBuilder folderSearchTreeBuilder,
        ILogger<FolderSearchViewModel> logger)
    {
        _folderSearchService = folderSearchService;
        _folderSearchContextProvider =
            folderSearchContextProvider;

        _folderSearchTreeBuilder =
            folderSearchTreeBuilder;

        _logger = logger;
    }

    public ObservableCollection<FolderSearchTreeNode>
        Results
    {
        get;
    } = [];

    public IReadOnlyList<SearchModeOption> SearchModes
    {
        get;
    } =
    [
        new(
            FolderSearchMode.Smart,
            "Умный поиск"),

        new(
            FolderSearchMode.Exact,
            "Точное вхождение")
    ];

    partial void OnSearchTextChanged(string value)
    {
        QueueSearch();
    }

    partial void OnSearchModeChanged(
        FolderSearchMode value)
    {
        QueueSearch();
    }

    private void QueueSearch()
    {
        if (_isDisposed)
        {
            return;
        }

        var version = Interlocked.Increment(
            ref _searchVersion);

        var cancellation = new CancellationTokenSource();

        var previousCancellation = Interlocked.Exchange(
            ref _searchCancellation,
            cancellation);

        previousCancellation?.Cancel();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Interlocked.CompareExchange(
                ref _searchCancellation,
                null,
                cancellation);

            cancellation.Cancel();
            cancellation.Dispose();

            Results.Clear();
            IsSearching = false;
            ResultsSummary =
                "Введите запрос для поиска по папкам";

            return;
        }

        IsSearching = true;
        ResultsSummary = "Поиск...";

        _ = SearchAfterDelayAsync(
            version,
            cancellation);
    }

    private async Task SearchAfterDelayAsync(
        int version,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                DebounceMilliseconds,
                cancellation.Token);

            var query = new FolderSearchQuery(
                SearchText,
                SearchMode,
                MaximumDisplayedResults);

            var matches =
                await _folderSearchService.SearchAsync(
                    query,
                    cancellation.Token);

            var contextFolders =
                await _folderSearchContextProvider
                    .GetContextFoldersAsync(
                        matches,
                        cancellation.Token);

            var resultTree =
                _folderSearchTreeBuilder.Build(
                    matches,
                    contextFolders);

            if (version != _searchVersion)
            {
                return;
            }

            Results.Clear();

            foreach (var rootNode in resultTree)
            {
                Results.Add(rootNode);
            }

            ResultsSummary = matches.Count == 0
                ? "Совпадений не найдено"
                : $"Найдено папок: {matches.Count}";
        }
        catch (OperationCanceledException)
        {
            // Отмена при вводе следующего символа ожидаема.
        }
        catch (Exception exception)
        {
            if (version != _searchVersion)
            {
                return;
            }

            Results.Clear();
            ResultsSummary =
                "Не удалось выполнить поиск";

            _logger.LogError(
                exception,
                "Could not update folder search results.");
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
            }

            Interlocked.CompareExchange(
                ref _searchCancellation,
                null,
                cancellation);

            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Interlocked.Increment(
            ref _searchVersion);

        var cancellation = Interlocked.Exchange(
            ref _searchCancellation,
            null);

        cancellation?.Cancel();
    }

    public sealed record SearchModeOption(
        FolderSearchMode Mode,
        string DisplayName);
}
