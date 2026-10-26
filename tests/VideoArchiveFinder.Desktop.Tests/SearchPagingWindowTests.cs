using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Desktop.Services;
using VideoArchiveFinder.Desktop.ViewModels;
using VideoArchiveFinder.Infrastructure;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;
using Xunit.Abstractions;

namespace VideoArchiveFinder.Desktop.Tests;

[CollectionDefinition("WpfMainWindow", DisableParallelization = true)]
public sealed class WpfMainWindowCollectionDefinition
{
}

[Collection("WpfMainWindow")]
public sealed class SearchPagingWindowTests
{
    private const int PageSize = 200;

    private const int TotalMatches = 633;

    private const string QueryText = "Needle";

    private const int SelectedLeafIndex = 190;

    private static readonly Guid RootSourceId =
        Guid.Parse("7f3c1d52-5d8e-4a6a-9f27-0d8b7f5e4c21");

    private static readonly int[] GroupSizes = [127, 127, 127, 126, 126];

    private static readonly string[] GroupNames =
        ["Group A", "Group B", "Group C", "Group D", "Group E"];

    private readonly ITestOutputHelper _output;

    public SearchPagingWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RealMainWindow_SearchPaging_PreservesTreeAndReactsToIndexChange()
    {
        await StaThread.RunAsync(
            async () =>
            {
                var tempRoot = Path.Combine(
                    Path.GetTempPath(),
                    "VAF-WpfMainWindow",
                    Guid.NewGuid().ToString("N"));

                var archiveRoot = Path.Combine(
                    tempRoot,
                    "Archive");

                Directory.CreateDirectory(archiveRoot);

                var services = new ServiceCollection();

                services.AddLogging(
                    builder => builder.SetMinimumLevel(
                        LogLevel.Warning));

                services.AddVideoArchiveFinderInfrastructure();

                services.RemoveAll<IFolderSearchPageService>();
                services.AddSingleton<SqliteFolderSearchPageService>();
                services.AddSingleton<IFolderSearchPageService>(
                    provider => new GatedFolderSearchPageService(
                        provider.GetRequiredService<
                            SqliteFolderSearchPageService>()));

                services.AddSingleton<
                    IThumbnailImageLoader,
                    WpfThumbnailImageLoader>();
                services.AddSingleton<
                    IWindowsShellService,
                    WindowsShellService>();
                services.AddSingleton<
                    IClipboardService,
                    WindowsClipboardService>();
                services.AddSingleton<
                    IAppThemeService,
                    AppThemeService>();
                services.AddSingleton<
                    ILocalFolderPicker,
                    WindowsLocalFolderPicker>();
                services.AddSingleton<
                    IUncPathInputDialog,
                    WindowsUncPathInputDialog>();
                services.AddSingleton<
                    IArchiveSourceIndexingModeDialog,
                    WindowsArchiveSourceIndexingModeDialog>();
                services.AddSingleton<
                    IArchiveSourceRemovalConfirmationDialog,
                    WindowsArchiveSourceRemovalConfirmationDialog>();
                services.AddSingleton<FolderSearchViewModel>();
                services.AddSingleton<FolderVideoFilesViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();

                services.RemoveAll<ILibVlcRuntimeLocator>();
                services.AddSingleton<ILibVlcRuntimeLocator>(
                    new NotReadyLibVlcRuntimeLocator());

                services.RemoveAll<IApplicationDataDirectoryProvider>();
                services.AddSingleton<IApplicationDataDirectoryProvider>(
                    new TemporaryApplicationDataDirectoryProvider(
                        tempRoot));

                ServiceProvider? provider = null;
                MainWindow? window = null;

                try
                {
                    provider = services.BuildServiceProvider();

                    await SeedIndexAsync(
                        provider,
                        archiveRoot,
                        tempRoot,
                        _output);

                    var viewModel =
                        provider.GetRequiredService<
                            MainWindowViewModel>();

                    await viewModel.InitializeAsync();

                    viewModel.Search.SetRootSourceIds(
                        [RootSourceId]);

                    EnsureApplication();

                    window =
                        provider.GetRequiredService<MainWindow>();

                    window.Width = 1180;
                    window.Height = 760;
                    window.WindowStartupLocation =
                        WindowStartupLocation.Manual;
                    window.Left = 0;
                    window.Top = 0;
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Opacity = 0;

                    window.Show();

                    await WaitUntilAsync(
                        () => window.IsLoaded,
                        "MainWindow did not load.");

                    await FlushUiAsync(window);

                    Assert.Same(
                        viewModel,
                        window.DataContext);

                    var tree = window.SearchResultsTree;
                    var loadMoreButton =
                        window.LoadMoreSearchResultsButton;
                    var refreshButton =
                        window.RefreshSearchResultsButton;
                    var searchTextBox = window.SearchTextBox;

                    Assert.Equal(
                        "Search.Results",
                        BindingPath(
                            tree,
                            ItemsControl.ItemsSourceProperty));

                    Assert.Same(
                        viewModel.Search.Results,
                        tree.ItemsSource);

                    Assert.Equal(
                        "Search.LoadMoreCommand",
                        BindingPath(
                            loadMoreButton,
                            ButtonBase.CommandProperty));

                    Assert.Same(
                        viewModel.Search.LoadMoreCommand,
                        loadMoreButton.Command);

                    Assert.Equal(
                        "Search.RefreshCommand",
                        BindingPath(
                            refreshButton,
                            ButtonBase.CommandProperty));

                    Assert.Same(
                        viewModel.Search.RefreshCommand,
                        refreshButton.Command);

                    Assert.Equal(
                        "Search.IsIndexChanged",
                        BindingPath(
                            refreshButton,
                            UIElement.VisibilityProperty));

                    Assert.Equal(
                        "Search.SearchText",
                        BindingPath(
                            searchTextBox,
                            TextBox.TextProperty));

                    Assert.Equal(
                        "Search.ResultsSummary",
                        BindingPath(
                            window.SearchResultsSummary,
                            TextBlock.TextProperty));

                    _output.WriteLine(
                        "Real MainWindow loaded; search bindings verified.");

                    searchTextBox.Text = QueryText;

                    await WaitUntilAsync(
                        () => viewModel.Search.SearchText == QueryText,
                        "SearchTextBox did not update Search.SearchText.");

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount ==
                            PageSize &&
                            !viewModel.Search.IsSearching,
                        "First real SQLite search page was not loaded.",
                        timeoutMilliseconds: 30000);

                    await WaitForUiAsync(
                        window,
                        () => loadMoreButton.Visibility ==
                            Visibility.Visible,
                        "Load-more button did not become visible.");

                    AssertPages(
                        viewModel,
                        tree,
                        PageSize);

                    Assert.Contains(
                        $"Найдено совпадений: {TotalMatches}",
                        viewModel.Search.ResultsSummary,
                        StringComparison.Ordinal);

                    Assert.Equal(
                        viewModel.Search.ResultsSummary,
                        window.SearchResultsSummary.Text);

                    var firstPageRoot =
                        Assert.Single(viewModel.Search.Results);

                    var groupA = Assert.Single(
                        firstPageRoot.Children,
                        child => child.Name == GroupNames[0]);

                    var groupB = Assert.Single(
                        firstPageRoot.Children,
                        child => child.Name == GroupNames[1]);

                    var selectedLeaf = Assert.Single(
                        groupB.Children,
                        child => child.Name ==
                            LeafName(SelectedLeafIndex));

                    CreatePhysicalFolder(
                        archiveRoot,
                        SelectedLeafIndex);

                    await SetExpandedAsync(
                        window,
                        tree,
                        groupA,
                        isExpanded: false);

                    Assert.False(groupA.IsExpanded);

                    await SelectItemAsync(
                        window,
                        tree,
                        selectedLeaf);

                    Assert.True(selectedLeaf.IsSelected);
                    Assert.Same(
                        selectedLeaf,
                        tree.SelectedItem);

                    await ScrollToItemAsync(
                        window,
                        tree,
                        selectedLeaf);

                    var scrollViewer =
                        FindVisualDescendants<ScrollViewer>(tree)
                            .FirstOrDefault();

                    Assert.NotNull(scrollViewer);

                    var scrollOffset =
                        scrollViewer!.VerticalOffset;

                    Assert.True(
                        scrollOffset > 0,
                        "The selected result was not scrolled away from the top.");

                    var state = new TreeState(
                        firstPageRoot,
                        groupA,
                        selectedLeaf,
                        scrollViewer,
                        scrollOffset);

                    _output.WriteLine(
                        $"Page 200: matches={MatchCount(viewModel)}, " +
                        $"collapsed={groupA.Name}, selected={selectedLeaf.Name}, " +
                        $"offset={scrollOffset:F1}.");

                    ClickButton(loadMoreButton);

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount == 400 &&
                            !viewModel.Search.IsLoadingMore,
                        "Second page was not loaded.",
                        timeoutMilliseconds: 30000);

                    await FlushUiAsync(window);

                    AssertPages(
                        viewModel,
                        tree,
                        400);

                    AssertTreeState(
                        viewModel,
                        tree,
                        state);

                    _output.WriteLine(
                        $"Page 400: matches={MatchCount(viewModel)}, " +
                        "tree state preserved.");

                    await MutateIndexAsync(
                        tempRoot,
                        RootSourceId);

                    ClickButton(loadMoreButton);

                    await WaitUntilAsync(
                        () => viewModel.Search.IsIndexChanged,
                        "Index change was not reported.",
                        timeoutMilliseconds: 30000);

                    await WaitForUiAsync(
                        window,
                        () => refreshButton.Visibility ==
                            Visibility.Visible &&
                            loadMoreButton.Visibility ==
                                Visibility.Collapsed &&
                            NoticeTextBlock(window)?.Visibility ==
                                Visibility.Visible,
                        "Index-change notice or refresh control is not visible.");

                    Assert.False(viewModel.Search.HasMoreResults);
                    Assert.Contains(
                        "Индекс изменился",
                        viewModel.Search.SearchNotice,
                        StringComparison.Ordinal);

                    Assert.Equal(
                        viewModel.Search.SearchNotice,
                        NoticeTextBlock(window)?.Text);

                    AssertPages(
                        viewModel,
                        tree,
                        400,
                        expectedHasMore: false);

                    AssertTreeState(
                        viewModel,
                        tree,
                        state);

                    _output.WriteLine(
                        "Index change: notice + refresh shown, " +
                        "400 results and tree state preserved.");

                    var gateEntered =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                    var releaseGate =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                    var pageService =
                        (GatedFolderSearchPageService)
                            provider.GetRequiredService<
                                IFolderSearchPageService>();

                    pageService.BeforeFirstPageAsync =
                        async cancellationToken =>
                        {
                            gateEntered.TrySetResult(true);

                            await releaseGate.Task.WaitAsync(
                                cancellationToken);
                        };

                    ClickButton(refreshButton);

                    await gateEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(15));

                    Assert.True(viewModel.Search.IsSearching);

                    var dispatcherTicked =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                    _ = window.Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(
                            () => dispatcherTicked.TrySetResult(true)));

                    await dispatcherTicked.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));

                    Assert.True(
                        viewModel.Search.SearchNotice.Length == 0);

                    _output.WriteLine(
                        "Dispatcher stayed responsive while the refresh search was pending.");

                    releaseGate.TrySetResult(true);

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount ==
                            PageSize &&
                            !viewModel.Search.IsSearching &&
                            !viewModel.Search.IsIndexChanged,
                        "Refreshed first page was not loaded.",
                        timeoutMilliseconds: 30000);

                    await WaitForUiAsync(
                        window,
                        () => refreshButton.Visibility ==
                                Visibility.Collapsed &&
                            loadMoreButton.Visibility ==
                                Visibility.Visible &&
                            NoticeTextBlock(window)?.Visibility ==
                                Visibility.Collapsed,
                        "Refresh controls did not return to the search state.");

                    AssertPages(
                        viewModel,
                        tree,
                        PageSize);

                    var refreshedRoot =
                        Assert.Single(viewModel.Search.Results);

                    var refreshedGroupA = Assert.Single(
                        refreshedRoot.Children,
                        child => child.Name == GroupNames[0]);

                    var refreshedGroupB = Assert.Single(
                        refreshedRoot.Children,
                        child => child.Name == GroupNames[1]);

                    var refreshedSelectedLeaf = Assert.Single(
                        refreshedGroupB.Children,
                        child => child.Name ==
                            LeafName(SelectedLeafIndex));

                    await SetExpandedAsync(
                        window,
                        tree,
                        refreshedGroupA,
                        isExpanded: false);

                    await SelectItemAsync(
                        window,
                        tree,
                        refreshedSelectedLeaf);

                    await ScrollToItemAsync(
                        window,
                        tree,
                        refreshedSelectedLeaf);

                    var refreshedScrollViewer =
                        FindVisualDescendants<ScrollViewer>(tree)
                            .FirstOrDefault();

                    Assert.NotNull(refreshedScrollViewer);

                    var refreshedOffset =
                        refreshedScrollViewer!.VerticalOffset;

                    Assert.True(refreshedOffset > 0);

                    var refreshedState = new TreeState(
                        refreshedRoot,
                        refreshedGroupA,
                        refreshedSelectedLeaf,
                        refreshedScrollViewer,
                        refreshedOffset);

                    ClickButton(loadMoreButton);

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount == 400 &&
                            !viewModel.Search.IsLoadingMore,
                        "Page 400 after refresh was not loaded.",
                        timeoutMilliseconds: 30000);

                    await FlushUiAsync(window);

                    AssertPages(
                        viewModel,
                        tree,
                        400);

                    AssertTreeState(
                        viewModel,
                        tree,
                        refreshedState);

                    ClickButton(loadMoreButton);

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount == 600 &&
                            !viewModel.Search.IsLoadingMore,
                        "Page 600 was not loaded.",
                        timeoutMilliseconds: 30000);

                    await FlushUiAsync(window);

                    AssertPages(
                        viewModel,
                        tree,
                        600);

                    AssertTreeState(
                        viewModel,
                        tree,
                        refreshedState);

                    ClickButton(loadMoreButton);

                    await WaitUntilAsync(
                        () => viewModel.Search.LoadedMatchCount ==
                                TotalMatches &&
                            !viewModel.Search.IsLoadingMore,
                        "Final page was not loaded.",
                        timeoutMilliseconds: 30000);

                    await WaitForUiAsync(
                        window,
                        () => loadMoreButton.Visibility ==
                                Visibility.Collapsed &&
                            !viewModel.Search.HasMoreResults,
                        "Final load-more button is still visible.");

                    await FlushUiAsync(window);

                    AssertPages(
                        viewModel,
                        tree,
                        TotalMatches);

                    AssertTreeState(
                        viewModel,
                        tree,
                        refreshedState);

                    Assert.False(
                        viewModel.Search.LoadMoreCommand.CanExecute(null));

                    var callsBeforeDisabledClick =
                        pageService.NextPageCallCount;

                    ClickButton(loadMoreButton);

                    await Task.Delay(150);

                    Assert.Equal(
                        callsBeforeDisabledClick,
                        pageService.NextPageCallCount);

                    Assert.Equal(
                        2,
                        pageService.FirstPageCallCount);

                    Assert.Equal(
                        5,
                        pageService.NextPageCallCount);

                    _output.WriteLine(
                        $"Page {TotalMatches}: final button hidden, no gaps/duplicates, " +
                        "tree state preserved, disabled click did nothing.");

                    await ActivateSelectedResultAsync(
                        window,
                        viewModel,
                        tree,
                        refreshedSelectedLeaf,
                        archiveRoot);

                    _output.WriteLine(
                        "Activation opened the synthetic temporary folder without an archive.");
                }
                finally
                {
                    if (window is not null)
                    {
                        if (window.IsLoaded)
                        {
                            window.Close();
                            await Task.Delay(300);
                        }
                    }

                    if (provider is not null)
                    {
                        await provider.DisposeAsync();
                    }

                    TryDeleteDirectory(tempRoot);
                }
            });
    }

    private static async Task SeedIndexAsync(
        IServiceProvider provider,
        string archiveRoot,
        string tempRoot,
        ITestOutputHelper output)
    {
        var initializer =
            provider.GetRequiredService<IIndexDatabaseInitializer>();

        await initializer.InitializeAsync();

        var repository =
            provider.GetRequiredService<IFolderIndexRepository>();

        var normalizer =
            provider.GetRequiredService<ITextNormalizationService>();

        var stemmer =
            provider.GetRequiredService<ISearchStemService>();

        var now = DateTimeOffset.UtcNow;

        var folders = new List<FolderIndexUpsertItem>();

        folders.Add(
            CreateUpsertItem(
                archiveRoot,
                "Archive",
                parentFullPath: null,
                normalizer,
                stemmer,
                now));

        var firstIndex = 1;

        for (var groupIndex = 0;
             groupIndex < GroupNames.Length;
             groupIndex++)
        {
            var groupPath = Path.Combine(
                archiveRoot,
                GroupNames[groupIndex]);

            Directory.CreateDirectory(groupPath);

            folders.Add(
                CreateUpsertItem(
                    groupPath,
                    GroupNames[groupIndex],
                    archiveRoot,
                    normalizer,
                    stemmer,
                    now));

            var lastIndex = firstIndex + GroupSizes[groupIndex] - 1;

            for (var leafIndex = firstIndex;
                 leafIndex <= lastIndex;
                 leafIndex++)
            {
                folders.Add(
                    CreateUpsertItem(
                        Path.Combine(
                            groupPath,
                            LeafName(leafIndex)),
                        LeafName(leafIndex),
                        groupPath,
                        normalizer,
                        stemmer,
                        now));
            }

            firstIndex = lastIndex + 1;
        }

        Assert.Equal(TotalMatches, firstIndex - 1);

        await repository.UpsertBatchAsync(folders);

        var databasePath =
            provider.GetRequiredService<IndexDatabasePathProvider>()
                .GetDatabasePath();

        await using var connection = new Microsoft.Data.Sqlite
            .SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM Folders
            WHERE instr(NormalizedName, 'needle') > 0;
            """;

        var matchCount = Convert.ToInt32(
            await command.ExecuteScalarAsync());

        Assert.Equal(TotalMatches, matchCount);

        output.WriteLine(
            $"Seeded {folders.Count} folders, {matchCount} synthetic matches " +
            $"in {tempRoot}.");
    }

    private static FolderIndexUpsertItem CreateUpsertItem(
        string fullPath,
        string name,
        string? parentFullPath,
        ITextNormalizationService normalizer,
        ISearchStemService stemmer,
        DateTimeOffset now)
    {
        var tokens = normalizer.Tokenize(name);

        return new FolderIndexUpsertItem(
            FullPath: fullPath,
            Name: name,
            NormalizedName: normalizer.Normalize(name),
            SearchTokens: normalizer.CreateTokenText(name),
            SearchStems: stemmer.CreateStemText(tokens),
            ParentFullPath: parentFullPath,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            LastSeenUtc: now,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static string LeafName(int index)
    {
        return $"Needle {index:0000}";
    }

    private static void CreatePhysicalFolder(
        string archiveRoot,
        int leafIndex)
    {
        var groupIndex = DetermineGroupIndex(leafIndex);

        var leafPath = Path.Combine(
            archiveRoot,
            GroupNames[groupIndex],
            LeafName(leafIndex));

        Directory.CreateDirectory(leafPath);
    }

    private static int DetermineGroupIndex(int leafIndex)
    {
        var boundary = 0;

        for (var groupIndex = 0;
             groupIndex < GroupSizes.Length;
             groupIndex++)
        {
            boundary += GroupSizes[groupIndex];

            if (leafIndex <= boundary)
            {
                return groupIndex;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(leafIndex));
    }

    private static async Task MutateIndexAsync(
        string tempRoot,
        Guid rootSourceId)
    {
        var databasePath = Path.Combine(
            tempRoot,
            "Index",
            "video-archive-finder.db");

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Folders
            (
                FullPath,
                Name,
                NormalizedName,
                SearchTokens,
                SearchStems,
                ParentFolderId,
                RootSourceId,
                IsAvailable,
                LastSeenUtc,
                DirectSubfolderCount,
                DirectVideoFileCount
            )
            VALUES
            (
                $fullPath,
                'Index mutation marker',
                'index mutation marker',
                ' index mutation marker ',
                ' index mutation marker ',
                NULL,
                $rootSourceId,
                1,
                $lastSeenUtc,
                0,
                0
            );
            """;

        command.Parameters.AddWithValue(
            "$fullPath",
            Path.Combine(
                tempRoot,
                "Archive",
                "Index mutation marker"));

        command.Parameters.AddWithValue(
            "$rootSourceId",
            rootSourceId.ToString("D"));

        command.Parameters.AddWithValue(
            "$lastSeenUtc",
            DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    private static void AssertPages(
        MainWindowViewModel viewModel,
        TreeView tree,
        int expectedLoaded,
        bool? expectedHasMore = null)
    {
        var hasMore =
            expectedHasMore ??
            expectedLoaded < TotalMatches;

        Assert.Equal(
            TotalMatches,
            viewModel.Search.TotalMatchCount);

        Assert.Equal(
            expectedLoaded,
            viewModel.Search.LoadedMatchCount);

        Assert.Equal(
            expectedLoaded,
            MatchCount(viewModel));

        var expectedNames = Enumerable
            .Range(1, expectedLoaded)
            .Select(LeafName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var actualNames = Flatten(viewModel.Search.Results)
            .Where(item => item.IsMatch)
            .Select(item => item.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
        Assert.Equal(
            actualNames.Length,
            actualNames.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            hasMore,
            viewModel.Search.HasMoreResults);

        Assert.Equal(
            hasMore,
            viewModel.Search.LoadMoreCommand.CanExecute(null));

        Assert.Same(
            viewModel.Search.Results,
            tree.ItemsSource);
    }

    private static int MatchCount(
        MainWindowViewModel viewModel)
    {
        return Flatten(viewModel.Search.Results)
            .Count(item => item.IsMatch);
    }

    private static IEnumerable<FolderSearchTreeItem> Flatten(
        IEnumerable<FolderSearchTreeItem> roots)
    {
        foreach (var root in roots)
        {
            yield return root;

            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }

    private static async Task SetExpandedAsync(
        Window window,
        TreeView tree,
        FolderSearchTreeItem item,
        bool isExpanded)
    {
        var container =
            await GetContainerAsync(
                window,
                tree,
                item);

        Assert.NotNull(container);

        container!.IsExpanded = isExpanded;

        await FlushUiAsync(window);

        Assert.Equal(
            isExpanded,
            item.IsExpanded);
    }

    private static async Task SelectItemAsync(
        Window window,
        TreeView tree,
        FolderSearchTreeItem item)
    {
        var container =
            await GetContainerAsync(
                window,
                tree,
                item);

        Assert.NotNull(container);

        container!.IsSelected = true;

        await FlushUiAsync(window);

        Assert.True(item.IsSelected);
        Assert.Same(item, tree.SelectedItem);
    }

    private static async Task ScrollToItemAsync(
        Window window,
        TreeView tree,
        FolderSearchTreeItem item)
    {
        var container =
            await GetContainerAsync(
                window,
                tree,
                item);

        Assert.NotNull(container);

        container!.BringIntoView();

        await FlushUiAsync(window);
    }

    private static async Task<TreeViewItem?> GetContainerAsync(
        Window window,
        TreeView tree,
        object item)
    {
        var container =
            FindTreeViewItem(tree, item);

        if (container is not null)
        {
            return container;
        }

        var scrollViewer =
            FindVisualDescendants<ScrollViewer>(tree)
                .FirstOrDefault();

        if (scrollViewer is null)
        {
            return null;
        }

        var originalOffset = scrollViewer.VerticalOffset;
        var maximumOffset = scrollViewer.ScrollableHeight;

        for (var offset = 0.0;
             offset <= maximumOffset;
             offset += 100.0)
        {
            scrollViewer.ScrollToVerticalOffset(offset);

            await FlushUiAsync(window);

            container =
                FindTreeViewItem(tree, item);

            if (container is not null)
            {
                return container;
            }
        }

        scrollViewer.ScrollToVerticalOffset(
            originalOffset);

        await FlushUiAsync(window);

        return FindTreeViewItem(tree, item);
    }

    private static TreeViewItem? FindTreeViewItem(
        ItemsControl parent,
        object item)
    {
        if (parent.ItemContainerGenerator
                .ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var child in
                 FindVisualDescendants<TreeViewItem>(parent))
        {
            if (ReferenceEquals(child.DataContext, item))
            {
                return child;
            }

            if (child.ItemContainerGenerator
                    .ContainerFromItem(item) is
                TreeViewItem nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void AssertTreeState(
        MainWindowViewModel viewModel,
        TreeView tree,
        TreeState state)
    {
        Assert.Same(
            state.Root,
            Assert.Single(viewModel.Search.Results));

        Assert.Contains(
            state.CollapsedGroup,
            Flatten(viewModel.Search.Results));

        Assert.Contains(
            state.Selected,
            Flatten(viewModel.Search.Results));

        Assert.False(state.CollapsedGroup.IsExpanded);
        Assert.True(state.Selected.IsSelected);
        Assert.Same(state.Selected, tree.SelectedItem);

        var currentScrollViewer =
            FindVisualDescendants<ScrollViewer>(tree)
                .FirstOrDefault();

        Assert.Same(
            state.ScrollViewer,
            currentScrollViewer);

        Assert.InRange(
            currentScrollViewer!.VerticalOffset,
            state.ScrollOffset - 1.0,
            state.ScrollOffset + 1.0);
    }

    private static async Task ActivateSelectedResultAsync(
        MainWindow window,
        MainWindowViewModel viewModel,
        TreeView tree,
        FolderSearchTreeItem item,
        string archiveRoot)
    {
        var container =
            await GetContainerAsync(
                window,
                tree,
                item);

        Assert.NotNull(container);

        var border = FindVisualDescendants<Border>(container!)
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ToolTip as string,
                    item.FullPath,
                    StringComparison.OrdinalIgnoreCase));

        border ??= FindVisualDescendants<Border>(container!)
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate.DataContext, item));

        Assert.NotNull(border);

        var mouse = Mouse.PrimaryDevice;

        var down = new MouseButtonEventArgs(
            mouse,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent =
                UIElement.PreviewMouseLeftButtonDownEvent
        };

        border!.RaiseEvent(down);

        var up = new MouseButtonEventArgs(
            mouse,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent =
                UIElement.PreviewMouseLeftButtonUpEvent
        };

        window.RaiseEvent(up);

        await WaitUntilAsync(
            () => string.Equals(
                item.FullPath,
                viewModel.VideoFiles.SelectedFolder?.FullPath,
                StringComparison.OrdinalIgnoreCase),
            "Folder activation did not select the synthetic folder.",
            timeoutMilliseconds: 15000);

        await WaitUntilAsync(
            () => !viewModel.VideoFiles.IsLoading &&
                (viewModel.VideoFiles.StatusText.StartsWith(
                     "Видеофайлов",
                     StringComparison.Ordinal) ||
                 viewModel.VideoFiles.StatusText.StartsWith(
                     "Не удалось",
                     StringComparison.Ordinal)),
            "Folder activation did not finish loading.",
            timeoutMilliseconds: 15000);

        Assert.Equal(
            Visibility.Visible,
            window.VideoFilesPanel.Visibility);

        Assert.DoesNotContain(
            "Не удалось",
            viewModel.VideoFiles.StatusText,
            StringComparison.Ordinal);

        Assert.StartsWith(
            archiveRoot,
            item.FullPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ClickButton(Button button)
    {
        Assert.NotNull(button.Command);

        button.Command.Execute(button.CommandParameter);
    }

    private static string? BindingPath(
        DependencyObject target,
        DependencyProperty property)
    {
        return BindingOperations
            .GetBindingExpression(target, property)?
            .ParentBinding.Path.Path;
    }

    private static TextBlock? NoticeTextBlock(
        DependencyObject root)
    {
        return FindVisualDescendants<TextBlock>(root)
            .FirstOrDefault(textBlock =>
                BindingPath(
                    textBlock,
                    TextBlock.TextProperty) ==
                "Search.SearchNotice");
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        var count =
            VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child =
                VisualTreeHelper.GetChild(root, index);

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in
                     FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task FlushUiAsync(Window window)
    {
        window.UpdateLayout();

        await Dispatcher.Yield(
            DispatcherPriority.ApplicationIdle);

        window.UpdateLayout();
    }

    private static async Task WaitForUiAsync(
        Window window,
        Func<bool> condition,
        string message,
        int timeoutMilliseconds = 15000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            await FlushUiAsync(window);

            if (condition())
            {
                return;
            }

            if (stopwatch.ElapsedMilliseconds >
                timeoutMilliseconds)
            {
                throw new TimeoutException(
                    $"Timed out: {message}");
            }

            await Task.Delay(10);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string message,
        int timeoutMilliseconds = 15000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            if (condition())
            {
                return;
            }

            if (stopwatch.ElapsedMilliseconds >
                timeoutMilliseconds)
            {
                throw new TimeoutException(
                    $"Timed out: {message}");
            }

            await Dispatcher.Yield(
                DispatcherPriority.Background);

            await Task.Delay(10);
        }
    }

    private static void EnsureApplication()
    {
        if (System.Windows.Application.Current is not null)
        {
            return;
        }

        var application = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        application.Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/VideoArchiveFinder;component/Themes/LightTheme.xaml")
            });

        application.Resources.MergedDictionaries.Add(
            LoadApplicationResources());

    }

    private static ResourceDictionary LoadApplicationResources()
    {
        var applicationXamlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VideoArchiveFinder.Desktop",
            "App.xaml");

        var document = XDocument.Load(applicationXamlPath);

        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var resources = document.Root?
            .Element(presentation + "Application.Resources")?
            .Element(presentation + "ResourceDictionary")
            ?? throw new InvalidOperationException(
                "App.xaml does not contain Application.Resources.");

        resources
            .Element(presentation +
                "ResourceDictionary.MergedDictionaries")?
            .Remove();

        var resourceElement = new XElement(resources);

        resourceElement.SetAttributeValue(
            XNamespace.Xmlns + "x",
            "http://schemas.microsoft.com/winfx/2006/xaml");

        resourceElement.SetAttributeValue(
            XNamespace.Xmlns + "primitives",
            "clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework");

        resourceElement.SetAttributeValue(
            "xmlns",
            presentation.NamespaceName);

        return (ResourceDictionary)XamlReader.Parse(
            resourceElement.ToString(
                SaveOptions.DisableFormatting));
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot =
            Environment.GetEnvironmentVariable(
                "VAF_TEST_REPO_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(
                Path.Combine(
                    configuredRoot,
                    "VideoArchiveFinder.sln")))
        {
            return Path.GetFullPath(configuredRoot);
        }

        var directory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "VideoArchiveFinder.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "VideoArchiveFinder.sln was not found above the test output directory.");
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(
                path,
                recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record TreeState(
        FolderSearchTreeItem Root,
        FolderSearchTreeItem CollapsedGroup,
        FolderSearchTreeItem Selected,
        ScrollViewer ScrollViewer,
        double ScrollOffset);

    private sealed class TemporaryApplicationDataDirectoryProvider
        : IApplicationDataDirectoryProvider
    {
        private readonly string _path;

        public TemporaryApplicationDataDirectoryProvider(
            string path)
        {
            _path = path;
        }

        public string GetApplicationDataDirectory()
        {
            return _path;
        }
    }

    private sealed class NotReadyLibVlcRuntimeLocator
        : ILibVlcRuntimeLocator
    {
        public LibVlcRuntimeStatus Locate()
        {
            return new LibVlcRuntimeStatus(
                RuntimeDirectory: string.Empty,
                LibVlcPath: string.Empty,
                LibVlcCorePath: string.Empty,
                PluginsDirectory: string.Empty,
                LibVlcExists: false,
                LibVlcCoreExists: false,
                PluginsExist: false);
        }
    }

    private sealed class GatedFolderSearchPageService
        : IFolderSearchPageService
    {
        private readonly IFolderSearchPageService _inner;

        private Func<CancellationToken, Task>?
            _beforeFirstPageAsync;

        private int _firstPageCallCount;

        private int _nextPageCallCount;

        public GatedFolderSearchPageService(
            IFolderSearchPageService inner)
        {
            _inner = inner;
        }

        public Func<CancellationToken, Task>?
            BeforeFirstPageAsync
        {
            get => _beforeFirstPageAsync;
            set => _beforeFirstPageAsync = value;
        }

        public int FirstPageCallCount =>
            Volatile.Read(ref _firstPageCallCount);

        public int NextPageCallCount =>
            Volatile.Read(ref _nextPageCallCount);

        public async Task<FolderSearchPage> SearchFirstPageAsync(
            FolderSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(
                ref _firstPageCallCount);

            var gate = Interlocked.Exchange(
                ref _beforeFirstPageAsync,
                null);

            if (gate is not null)
            {
                await gate(cancellationToken);
            }

            return await _inner.SearchFirstPageAsync(
                query,
                cancellationToken);
        }

        public Task<FolderSearchPage> SearchNextPageAsync(
            FolderSearchSession session,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(
                ref _nextPageCallCount);

            return _inner.SearchNextPageAsync(
                session,
                cancellationToken);
        }

        public void ReleaseSession(
            FolderSearchSession session)
        {
            _inner.ReleaseSession(session);
        }

        public Task ValidateSessionAsync(
            FolderSearchSession session,
            CancellationToken cancellationToken = default)
        {
            return _inner.ValidateSessionAsync(
                session,
                cancellationToken);
        }
    }
}

internal static class StaThread
{
    public static Task RunAsync(
        Func<Task> action,
        TimeSpan? timeout = null)
    {
        var completion =
            new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(
            () =>
            {
                Exception? failure = null;
                var dispatcher = Dispatcher.CurrentDispatcher;

                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(
                        dispatcher));

                try
                {
                    var task = action();

                    task.ContinueWith(
                        completed =>
                        {
                            if (completed.IsFaulted)
                            {
                                failure =
                                    completed.Exception?
                                        .GetBaseException();
                            }
                            else if (completed.IsCanceled)
                            {
                                failure =
                                    new TaskCanceledException(
                                        completed);
                            }

                            dispatcher.BeginInvoke(
                                DispatcherPriority.Send,
                                new Action(
                                    dispatcher.InvokeShutdown));
                        },
                        TaskScheduler.Default);

                    Dispatcher.Run();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    completion.TrySetResult(failure);
                }
            });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        var effectiveTimeout =
            timeout ?? TimeSpan.FromMinutes(3);

        if (!completion.Task.Wait(effectiveTimeout))
        {
            throw new TimeoutException(
                "The STA test thread did not finish in time.");
        }

        if (completion.Task.Result is { } captured)
        {
            ExceptionDispatchInfo
                .Capture(captured)
                .Throw();
        }

        return Task.CompletedTask;
    }
}
