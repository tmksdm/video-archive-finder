using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.ViewModels;

/// <summary>
/// Модель представления результатов поиска.
///
/// Первую порцию выдаёт <see cref="IFolderSearchPageService"/>, следующие
/// порции догружает команда «Показать ещё» по курсору сессии. Счётчики берутся
/// из порции, то есть считают совпадения, а не контекстные узлы дерева.
///
/// Дерево дополняется только узлами очередной порции: уже показанные элементы,
/// их раскрытие и выделение сохраняются.
/// </summary>
public partial class FolderSearchViewModel
    : ObservableObject, IDisposable
{
    private const int DebounceMilliseconds = 250;

    /// <summary>
    /// Размер порции: столько же, сколько показывал прежний поиск.
    /// </summary>
    private const int SearchPageSize = 200;

    private readonly IFolderSearchPageService
        _folderSearchPageService;

    private readonly IFolderSearchContextProvider
        _folderSearchContextProvider;

    private readonly FolderSearchTreeBuilder
        _folderSearchTreeBuilder;

    private readonly ILogger<FolderSearchViewModel>
        _logger;

    private readonly int _debounceMilliseconds;

    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyCollection<Guid> _rootSourceIds = [];
    private IReadOnlyCollection<Guid> _videoFileRootSourceIds = [];

    private readonly FolderSearchTreeMerger _treeMerger = new();

    private FolderSearchSession? _session;
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
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string _resultsSummary =
        "Введите запрос для поиска";

    [ObservableProperty]
    private int _totalMatchCount;

    [ObservableProperty]
    private int _loadedMatchCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMoreResults;

    [ObservableProperty]
    private string _searchNotice = string.Empty;

    [ObservableProperty]
    private bool _isIndexChanged;

    public FolderSearchViewModel(
        IFolderSearchPageService folderSearchPageService,
        IFolderSearchContextProvider
            folderSearchContextProvider,
        FolderSearchTreeBuilder folderSearchTreeBuilder,
        ILogger<FolderSearchViewModel> logger)
        : this(
            folderSearchPageService,
            folderSearchContextProvider,
            folderSearchTreeBuilder,
            logger,
            DebounceMilliseconds)
    {
    }

    /// <summary>
    /// Конструктор с настраиваемой задержкой ввода. Продуктив создаётся через
    /// внедрение зависимостей без последнего аргумента; тесты задают нулевую
    /// задержку, поэтому проверяют логику, а не ожидание debounce.
    /// </summary>
    internal FolderSearchViewModel(
        IFolderSearchPageService folderSearchPageService,
        IFolderSearchContextProvider
            folderSearchContextProvider,
        FolderSearchTreeBuilder folderSearchTreeBuilder,
        ILogger<FolderSearchViewModel> logger,
        int debounceMilliseconds)
    {
        _folderSearchPageService =
            folderSearchPageService;

        _folderSearchContextProvider =
            folderSearchContextProvider;

        _folderSearchTreeBuilder =
            folderSearchTreeBuilder;

        _logger = logger;
        _debounceMilliseconds = debounceMilliseconds;
    }

    public ObservableCollection<FolderSearchTreeItem> Results =>
        _treeMerger.Roots;

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

    public void SetRootSourceIds(
        IEnumerable<Guid> rootSourceIds,
        IEnumerable<Guid>? videoFileRootSourceIds = null)
    {
        ArgumentNullException.ThrowIfNull(rootSourceIds);

        // Область поиска всегда передаётся явными коллекциями: пустая означает
        // «по этому потоку не искать», а не «без ограничения по источникам»
        // (null в FolderSearchQuery значил бы последнее).
        _rootSourceIds = rootSourceIds
            .Distinct()
            .ToArray();

        _videoFileRootSourceIds = videoFileRootSourceIds?
            .Distinct()
            .ToArray() ?? [];

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
        previousCancellation?.Dispose();

        ResetSearchState();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ReleaseCancellation(cancellation);

            ResultsSummary =
                "Введите запрос для поиска";

            return;
        }

        if (_rootSourceIds.Count == 0 &&
            _videoFileRootSourceIds.Count == 0)
        {
            ReleaseCancellation(cancellation);

            ResultsSummary =
                "Выберите хотя бы один источник для поиска";

            return;
        }

        IsSearching = true;
        ResultsSummary = "Поиск...";

        _ = SearchAfterDelayAsync(
            version,
            cancellation.Token);
    }

    private async Task SearchAfterDelayAsync(
        int version,
        CancellationToken cancellationToken)
    {
        FolderSearchSession? receivedSession = null;

        try
        {
            await Task.Delay(
                _debounceMilliseconds,
                cancellationToken);

            var query = new FolderSearchQuery(
                SearchText,
                SearchMode,
                SearchPageSize,
                _rootSourceIds,
                _videoFileRootSourceIds);

            var (page, resultTree) = await ReadPageTreeAsync(
                query,
                session: null,
                cancellationToken);

            receivedSession = page.NextSession;

            if (version != _searchVersion ||
                _isDisposed)
            {
                return;
            }

            PublishResults(
                resultTree,
                page.TotalMatchCount,
                page.LoadedMatchCount,
                page.HasMoreResults);

            if (page.HasMoreResults)
            {
                _session = receivedSession;
                receivedSession = null;
            }
        }
        catch (FolderSearchIndexChangedException)
        {
            if (version == _searchVersion && !_isDisposed)
            {
                StopForIndexChange();
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена при вводе следующего символа ожидаема.
        }
        catch (Exception exception)
        {
            if (version != _searchVersion ||
                _isDisposed)
            {
                return;
            }

            ResetSearchState();
            ResultsSummary = "Не удалось выполнить поиск";

            _logger.LogError(
                exception,
                "Could not update folder search results.");
        }
        finally
        {
            if (receivedSession is not null)
            {
                _folderSearchPageService.ReleaseSession(receivedSession);
            }

            if (version == _searchVersion &&
                !_isDisposed)
            {
                IsSearching = false;
            }
        }
    }

    private bool CanLoadMore()
    {
        return !_isDisposed &&
               !IsLoadingMore &&
               HasMoreResults;
    }

    [RelayCommand]
    private void Refresh()
    {
        QueueSearch();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        var session = _session;
        var cancellation = _searchCancellation;

        // Явная защита от повторного запуска: прямой вызов Execute не сверяется
        // с CanExecute, поэтому одной проверки команды недостаточно.
        if (_isDisposed ||
            IsLoadingMore ||
            session is null ||
            cancellation is null ||
            !HasMoreResults)
        {
            return;
        }

        var version = _searchVersion;
        var cancellationToken = cancellation.Token;

        IsLoadingMore = true;
        SearchNotice = string.Empty;

        try
        {
            var query = new FolderSearchQuery(
                session.QueryText,
                session.Mode,
                session.PageSize,
                session.RootSourceIds,
                session.VideoFileRootSourceIds);

            var (page, resultTree) = await ReadPageTreeAsync(
                query,
                session,
                cancellationToken);

            if (version != _searchVersion ||
                _isDisposed)
            {
                return;
            }

            PublishResults(
                resultTree,
                page.TotalMatchCount,
                page.LoadedMatchCount,
                page.HasMoreResults);

            _session = page.HasMoreResults ? page.NextSession : null;

            if (!page.HasMoreResults)
            {
                _folderSearchPageService.ReleaseSession(session);
            }
        }
        catch (FolderSearchIndexChangedException)
        {
            if (version == _searchVersion && !_isDisposed)
            {
                StopForIndexChange();
            }
        }
        catch (OperationCanceledException)
        {
            // Подгрузка отменена новым запросом.
        }
        catch (Exception exception)
        {
            if (version != _searchVersion ||
                _isDisposed)
            {
                return;
            }

            // Показанное дерево и счётчики сохраняются: неудачная подгрузка
            // не должна скрывать уже найденные совпадения.
            SearchNotice = "Не удалось загрузить следующую порцию. " +
                "Повторите попытку кнопкой «Показать ещё».";

            _logger.LogError(
                exception,
                "Could not load the next folder search page.");
        }
        finally
        {
            if (version == _searchVersion &&
                !_isDisposed)
            {
                IsLoadingMore = false;
            }
        }
    }

    private void PublishResults(
        IReadOnlyList<FolderSearchTreeNode> resultTree,
        int totalMatchCount,
        int loadedMatchCount,
        bool hasMoreResults)
    {
        _treeMerger.Append(resultTree);

        TotalMatchCount = totalMatchCount;
        LoadedMatchCount = loadedMatchCount;
        HasMoreResults = hasMoreResults;

        ResultsSummary = totalMatchCount == 0
            ? "Совпадений не найдено"
            : $"Найдено совпадений: {totalMatchCount}; " +
              $"показано {loadedMatchCount}";
    }

    private void ResetSearchState()
    {
        if (_session is not null)
        {
            _folderSearchPageService.ReleaseSession(_session);
        }

        _session = null;

        TotalMatchCount = 0;
        LoadedMatchCount = 0;
        HasMoreResults = false;
        IsLoadingMore = false;
        IsSearching = false;
        IsIndexChanged = false;
        SearchNotice = string.Empty;

        _treeMerger.Clear();
    }

    private Task<(FolderSearchPage Page, IReadOnlyList<FolderSearchTreeNode> Tree)>
        ReadPageTreeAsync(
            FolderSearchQuery query,
            FolderSearchSession? session,
            CancellationToken cancellationToken)
    {
        // SQLite's async API can execute synchronously. Keep both SQL and
        // per-page tree construction off the dispatcher; only Append touches UI.
        return Task.Run(async () =>
        {
            var page = session is null
                ? await _folderSearchPageService.SearchFirstPageAsync(
                    query, cancellationToken)
                : await _folderSearchPageService.SearchNextPageAsync(
                    session, cancellationToken);

            try
            {
                var context = await _folderSearchContextProvider
                    .GetContextFoldersAsync(page.Matches, cancellationToken);

                var tree = _folderSearchTreeBuilder.Build(
                    page.Matches, context, query.Text, query.Mode);

                await _folderSearchPageService.ValidateSessionAsync(
                    page.NextSession, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                return (page, tree);
            }
            catch
            {
                if (session is null)
                {
                    _folderSearchPageService.ReleaseSession(page.NextSession);
                }

                throw;
            }
        }, cancellationToken);
    }

    private void StopForIndexChange()
    {
        if (_session is not null)
        {
            _folderSearchPageService.ReleaseSession(_session);
            _session = null;
        }

        HasMoreResults = false;
        IsIndexChanged = true;

        if (LoadedMatchCount == 0)
        {
            ResultsSummary = "Поиск остановлен: индекс изменился";
        }

        SearchNotice = (LoadedMatchCount > 0
            ? "Индекс изменился. Найденные результаты сохранены. "
            : "Индекс изменился до завершения поиска. ") +
            "Нажмите «Обновить поиск», чтобы получить актуальную выдачу.";
    }

    private void ReleaseCancellation(
        CancellationTokenSource cancellation)
    {
        Interlocked.CompareExchange(
            ref _searchCancellation,
            null,
            cancellation);

        cancellation.Cancel();
        cancellation.Dispose();
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
        cancellation?.Dispose();

        if (_session is not null)
        {
            _folderSearchPageService.ReleaseSession(_session);
            _session = null;
        }
    }

    public sealed record SearchModeOption(
        FolderSearchMode Mode,
        string DisplayName);
}
