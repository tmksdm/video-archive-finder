using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop;

public partial class MainWindow : Window
{
    private bool _isSynchronizingVideoSelection;

    private bool _isArchiveSourcesFlyoutOpen;

    private GridLength _videoModeSearchResultsWidth =
        new(1.4, GridUnitType.Star);

    private GridLength _videoModeVideoFilesWidth =
        new(3.6, GridUnitType.Star);

    private bool _isSearchResultsPanelHidden;


    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ToggleArchiveSourcesFlyout_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetArchiveSourcesFlyoutOpen(
            !_isArchiveSourcesFlyoutOpen);
    }

    private void ArchiveSourcesDismissLayer_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        SetArchiveSourcesFlyoutOpen(false);
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            !_isArchiveSourcesFlyoutOpen)
        {
            return;
        }

        SetArchiveSourcesFlyoutOpen(false);
        e.Handled = true;
    }

    private void SetArchiveSourcesFlyoutOpen(bool isOpen)
    {
        _isArchiveSourcesFlyoutOpen = isOpen;

        ArchiveSourcesDismissLayer.Visibility =
            isOpen
                ? Visibility.Visible
                : Visibility.Collapsed;

        ArchiveSourcesFlyout.Visibility =
            isOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
    }


    private void ArchiveSourceCard_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject sourceElement)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(
                ArchiveSourcesList,
                sourceElement) is not ListBoxItem sourceItem)
        {
            return;
        }

        if (!sourceItem.IsSelected)
        {
            ArchiveSourcesList.SelectedItems.Clear();
            sourceItem.IsSelected = true;
        }

        sourceItem.Focus();
    }

    private async void ArchiveSourcesList_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.A &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ArchiveSourcesList.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        e.Handled = true;
        await RemoveSelectedArchiveSourcesAsync();
    }

    private async void RemoveSelectedArchiveSources_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RemoveSelectedArchiveSourcesAsync();
    }

    private async Task RemoveSelectedArchiveSourcesAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var selectedSources = ArchiveSourcesList.SelectedItems
            .OfType<ArchiveSourceItemViewModel>()
            .ToList();

        await viewModel.RemoveSourcesAsync(selectedSources);
    }

    private async void FolderSearchNode_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement element ||
            element.DataContext is not
                FolderSearchTreeNode selectedFolder)
        {
            return;
        }

        SetVideoFilesPanelVisible(true);

        await viewModel.VideoFiles.SelectFolderAsync(
            selectedFolder);
    }


    private void ReturnToSearchResults_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetVideoFilesPanelVisible(false);
    }

    private void ToggleSearchResultsPanel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSearchResultsPanelHidden)
        {
            _isSearchResultsPanelHidden = false;
            ApplyVideoModeLayout();
            return;
        }

        RememberVideoModeColumnWidths();

        _isSearchResultsPanelHidden = true;
        ApplyVideoModeLayout();
    }


    private void ShowGridViewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FocusVideoFilesControl(VideoFilesGrid);
    }

    private void ShowListViewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FocusVideoFilesControl(VideoFilesList);
    }

    private void GridCardWidthSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        FocusVideoFilesControl(VideoFilesGrid);
    }


    private void FocusVideoFilesControl(
        ListBox videoFilesControl)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(
                () => videoFilesControl.Focus()));
    }


    private void VideoFiles_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not ListBox videoFilesList ||
            e.Key != Key.A ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        videoFilesList.SelectAll();
        e.Handled = true;
    }

    private void VideoFiles_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizingVideoSelection ||
            sender is not ListBox sourceList)
        {
            return;
        }

        var targetList = ReferenceEquals(
            sourceList,
            VideoFilesGrid)
                ? VideoFilesList
                : VideoFilesGrid;

        try
        {
            _isSynchronizingVideoSelection = true;

            foreach (var removedItem in e.RemovedItems)
            {
                targetList.SelectedItems.Remove(
                    removedItem);
            }

            foreach (var addedItem in e.AddedItems)
            {
                if (!targetList.SelectedItems.Contains(
                        addedItem))
                {
                    targetList.SelectedItems.Add(
                        addedItem);
                }
            }
        }
        finally
        {
            _isSynchronizingVideoSelection = false;
        }
    }


    private void VideoFiles_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not ListBox videoFilesList ||
            e.OriginalSource is not DependencyObject sourceElement ||
            ItemsControl.ContainerFromElement(
                videoFilesList,
                sourceElement) is not ListBoxItem videoItem ||
            videoItem.DataContext is not IndexedVideoFile videoFile)
        {
            return;
        }

        if (viewModel.VideoFiles.OpenVideoCommand.CanExecute(
                videoFile))
        {
            viewModel.VideoFiles.OpenVideoCommand.Execute(
                videoFile);
        }

        e.Handled = true;
    }



    private void SetVideoFilesPanelVisible(bool isVisible)
    {
        if (isVisible)
        {
            VideoFilesPanel.Visibility = Visibility.Visible;
            ApplyVideoModeLayout();
            return;
        }

        RememberVideoModeColumnWidths();

        SearchResultsPanel.Visibility = Visibility.Visible;
        SearchResultsColumn.Width =
            new GridLength(1, GridUnitType.Star);

        SearchResultsSplitterColumn.Width =
            new GridLength(0);

        SearchResultsSplitter.Visibility =
            Visibility.Collapsed;

        VideoFilesColumn.Width =
            new GridLength(0);

        VideoFilesPanel.Visibility =
            Visibility.Collapsed;
    }

    private void ApplyVideoModeLayout()
    {
        VideoFilesPanel.Visibility = Visibility.Visible;

        SearchResultsPanel.Visibility =
            _isSearchResultsPanelHidden
                ? Visibility.Collapsed
                : Visibility.Visible;

        SearchResultsColumn.Width =
            _isSearchResultsPanelHidden
                ? new GridLength(0)
                : _videoModeSearchResultsWidth;

        SearchResultsSplitterColumn.Width =
            _isSearchResultsPanelHidden
                ? new GridLength(0)
                : new GridLength(6);

        SearchResultsSplitter.Visibility =
            _isSearchResultsPanelHidden
                ? Visibility.Collapsed
                : Visibility.Visible;

        VideoFilesColumn.Width =
            _isSearchResultsPanelHidden
                ? new GridLength(1, GridUnitType.Star)
                : _videoModeVideoFilesWidth;

        CollapseSearchResultsIcon.Visibility =
            _isSearchResultsPanelHidden
                ? Visibility.Collapsed
                : Visibility.Visible;

        ExpandSearchResultsIcon.Visibility =
            _isSearchResultsPanelHidden
                ? Visibility.Visible
                : Visibility.Collapsed;

        ToggleSearchResultsButton.ToolTip =
            _isSearchResultsPanelHidden
                ? "Показать дерево результатов"
                : "Скрыть дерево результатов";
    }

    private void RememberVideoModeColumnWidths()
    {
        if (_isSearchResultsPanelHidden ||
            VideoFilesPanel.Visibility != Visibility.Visible ||
            SearchResultsColumn.ActualWidth <= 0 ||
            VideoFilesColumn.ActualWidth <= 0)
        {
            return;
        }

        _videoModeSearchResultsWidth =
            SearchResultsColumn.Width;

        _videoModeVideoFilesWidth =
            VideoFilesColumn.Width;
    }







}
