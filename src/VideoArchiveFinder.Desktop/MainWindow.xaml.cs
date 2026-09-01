using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Desktop.Services;
using VideoArchiveFinder.Desktop.ViewModels;
using VideoArchiveFinder.Desktop.Views;


namespace VideoArchiveFinder.Desktop;

public partial class MainWindow : Window
{
    private readonly IAppThemeService
        _appThemeService;

    private readonly IThumbnailCacheService
        _thumbnailCacheService;

    private readonly IWindowsShellService
        _windowsShellService;

    private readonly Microsoft.Extensions.Logging
        .ILogger<CacheSettingsDialog>
        _cacheDialogLogger;

    private bool _isSynchronizingVideoSelection;

    private Point? _videoDragStartPoint;

    private ListBox? _videoDragSourceList;

    private VideoFileCardViewModel? _videoDragCandidate;

    private IReadOnlyList<VideoFileCardViewModel>?
        _videoDragSelectionAtMouseDown;

    private Point? _folderDragStartPoint;

    private FolderSearchTreeNode? _folderDragCandidate;

    private ListBox? _videoAreaSelectionSource;

    private Point? _videoAreaSelectionStartPoint;

    private IReadOnlySet<VideoFileCardViewModel>?
        _videoAreaSelectionBaseline;

    private bool _isVideoAreaSelecting;

    private bool _isArchiveSourcesFlyoutOpen;

    private GridLength _videoModeSearchResultsWidth =
        new(1.4, GridUnitType.Star);

    private GridLength _videoModeVideoFilesWidth =
        new(3.6, GridUnitType.Star);

    private bool _isSearchResultsPanelHidden;


    public MainWindow(
        MainWindowViewModel viewModel,
        IAppThemeService appThemeService,
        IThumbnailCacheService thumbnailCacheService,
        IWindowsShellService windowsShellService,
        ILibVlcRuntimeLocator libVlcRuntimeLocator,
        Microsoft.Extensions.Logging.ILogger<MainWindow>
            hoverScrubLogger,
        Microsoft.Extensions.Logging.ILogger<
            CacheSettingsDialog> cacheDialogLogger)
    {
        _appThemeService = appThemeService;
        _thumbnailCacheService = thumbnailCacheService;
        _windowsShellService = windowsShellService;
        _libVlcRuntimeLocator = libVlcRuntimeLocator;
        _hoverScrubLogger = hoverScrubLogger;
        _cacheDialogLogger = cacheDialogLogger;

        InitializeComponent();
        InitializeHoverScrubbing();

        DataContext = viewModel;

        _appThemeService.ThemeChanged +=
            AppThemeService_ThemeChanged;

        Closed +=
            MainWindow_Closed;

        UpdateThemeMenuChecks();
        UpdateCaptionState();
    }

    private void MinimizeCaptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreCaptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void CloseCaptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void MainWindow_StateChanged(
        object? sender,
        EventArgs e)
    {
        UpdateCaptionState();
    }

    private void UpdateCaptionState()
    {
        if (MaximizeCaptionIcon is null ||
            RestoreCaptionIcon is null ||
            MaximizeRestoreCaptionButton is null)
        {
            return;
        }

        var isMaximized =
            WindowState == WindowState.Maximized;

        MaximizeCaptionIcon.Visibility =
            isMaximized
                ? Visibility.Collapsed
                : Visibility.Visible;

        RestoreCaptionIcon.Visibility =
            isMaximized
                ? Visibility.Visible
                : Visibility.Collapsed;

        MaximizeRestoreCaptionButton.ToolTip =
            isMaximized
                ? "Восстановить"
                : "Развернуть";
    }


    private void OpenSettingsMenu_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateThemeMenuChecks();

        SettingsContextMenu.PlacementTarget =
            SettingsButton;

        SettingsContextMenu.IsOpen = true;
    }

    private async void SetSystemTheme_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SetThemeAsync(
            AppThemeMode.System);
    }

    private async void SetLightTheme_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SetThemeAsync(
            AppThemeMode.Light);
    }

    private async void SetDarkTheme_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SetThemeAsync(
            AppThemeMode.Dark);
    }

    private async Task SetThemeAsync(
        AppThemeMode mode)
    {
        try
        {
            await _appThemeService.SetThemeAsync(
                mode);

            UpdateThemeMenuChecks();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Не удалось изменить тему приложения.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Video Archive Finder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateThemeMenuChecks()
    {
        SystemThemeMenuItem.IsChecked =
            _appThemeService.SelectedMode ==
            AppThemeMode.System;

        LightThemeMenuItem.IsChecked =
            _appThemeService.SelectedMode ==
            AppThemeMode.Light;

        DarkThemeMenuItem.IsChecked =
            _appThemeService.SelectedMode ==
            AppThemeMode.Dark;
    }

    private void AppThemeService_ThemeChanged(
        object? sender,
        EventArgs e)
    {
        UpdateThemeMenuChecks();
    }

    private void OpenThumbnailCacheDialog_Click(
        object sender,
        RoutedEventArgs e)
    {
        SettingsContextMenu.IsOpen = false;

        var dialog = new CacheSettingsDialog(
            _thumbnailCacheService,
            _windowsShellService,
            _cacheDialogLogger);

        dialog.Owner = this;

        dialog.ShowDialog();

        if (!dialog.WasCacheCleared ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _ = viewModel.VideoFiles.SelectFolderAsync(
            viewModel.VideoFiles.SelectedFolder);
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        DisposeHoverScrubbing();

        _appThemeService.ThemeChanged -=
            AppThemeService_ThemeChanged;

        Closed -=
            MainWindow_Closed;
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

    private void FolderSearchNode_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ResetDragCandidates();

        if (sender is not FrameworkElement element ||
            element.DataContext is not
                FolderSearchTreeNode selectedFolder)
        {
            return;
        }

        _folderDragStartPoint = e.GetPosition(this);
        _folderDragCandidate = selectedFolder;
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

    private void VideoFiles_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ResetDragCandidates();

        if (sender is not ListBox videoFilesList ||
            e.OriginalSource is not DependencyObject sourceElement ||
            FindVisualAncestor<ScrollBar>(sourceElement) is not null)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(
                videoFilesList,
                sourceElement) is not ListBoxItem videoItem ||
            videoItem.DataContext is not
                VideoFileCardViewModel videoFile)
        {
            StartVideoAreaSelection(
                videoFilesList,
                e);

            return;
        }

        PrepareVideoFileDrag(
            videoFilesList,
            videoFile,
            e.GetPosition(this));
    }

    private void HoverScrubInputLayer_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_hoverScrubVideoFile is not { } videoFile)
        {
            return;
        }

        var sourceList = VideoFilesGrid.Visibility ==
                Visibility.Visible
            ? VideoFilesGrid
            : VideoFilesList;

        var startPoint = e.GetPosition(this);

        StopHoverScrubbing();
        ResetDragCandidates();

        var isControlPressed =
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control);

        if (isControlPressed &&
            sourceList.SelectedItems.Contains(videoFile))
        {
            sourceList.SelectedItems.Remove(videoFile);
            e.Handled = true;
            return;
        }

        if (!sourceList.SelectedItems.Contains(videoFile))
        {
            if (!isControlPressed)
            {
                sourceList.UnselectAll();
            }

            sourceList.SelectedItems.Add(videoFile);
        }

        PrepareVideoFileDrag(
            sourceList,
            videoFile,
            startPoint);

        e.Handled = true;
    }

    private void PrepareVideoFileDrag(
        ListBox videoFilesList,
        VideoFileCardViewModel videoFile,
        Point startPoint)
    {
        _videoDragStartPoint = startPoint;
        _videoDragSourceList = videoFilesList;
        _videoDragCandidate = videoFile;
        _videoDragSelectionAtMouseDown = videoFilesList.Items
            .OfType<VideoFileCardViewModel>()
            .Where(videoFilesList.SelectedItems.Contains)
            .ToArray();
    }

    private void DragSource_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndVideoAreaSelection();
            ResetDragCandidates();
            return;
        }

        if (TryUpdateVideoAreaSelection(e))
        {
            return;
        }

        if (TryStartFolderDrag(e))
        {
            return;
        }

        if (_videoDragStartPoint is not Point startPoint ||
            _videoDragSourceList is not ListBox sourceList ||
            _videoDragCandidate is not { } candidate)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);

        if (Math.Abs(currentPoint.X - startPoint.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - startPoint.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var selectionAtMouseDown =
            _videoDragSelectionAtMouseDown;

        var restorePreviousSelection =
            selectionAtMouseDown?.Contains(candidate) == true;

        ResetDragCandidates();

        if (restorePreviousSelection)
        {
            sourceList.SelectedItems.Clear();

            foreach (var selectedFile in selectionAtMouseDown!)
            {
                sourceList.SelectedItems.Add(selectedFile);
            }
        }

        if (!sourceList.SelectedItems.Contains(candidate))
        {
            return;
        }

        var selectedFiles = sourceList.Items
            .OfType<VideoFileCardViewModel>()
            .Where(sourceList.SelectedItems.Contains)
            .Select(file => file.VideoFile);

        var dragSelection = VideoFileDragPathResolver.Resolve(
            selectedFiles,
            File.Exists);

        if (dragSelection.Paths.Count == 0)
        {
            if (DataContext is MainWindowViewModel emptyViewModel)
            {
                emptyViewModel.VideoFiles.StatusText =
                    "Выбранные видеофайлы недоступны для переноса";
            }

            return;
        }

        if (dragSelection.UnavailableCount > 0 &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.VideoFiles.StatusText =
                $"Недоступные файлы пропущены: {dragSelection.UnavailableCount}";
        }

        var data = new DataObject(
            DataFormats.FileDrop,
            dragSelection.Paths.ToArray());

        e.Handled = true;

        DragDrop.DoDragDrop(
            sourceList,
            data,
            DragDropEffects.Copy);
    }

    private void StartVideoAreaSelection(
        ListBox sourceList,
        MouseButtonEventArgs e)
    {
        EndVideoAreaSelection();

        _videoAreaSelectionSource = sourceList;
        _videoAreaSelectionStartPoint =
            e.GetPosition(VideoAreaSelectionOverlay);

        _videoAreaSelectionBaseline =
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? sourceList.SelectedItems
                    .OfType<VideoFileCardViewModel>()
                    .ToHashSet()
                : new HashSet<VideoFileCardViewModel>();

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            sourceList.UnselectAll();
        }

        sourceList.CaptureMouse();
        e.Handled = true;
    }

    private bool TryUpdateVideoAreaSelection(
        MouseEventArgs e)
    {
        if (_videoAreaSelectionSource is not ListBox sourceList ||
            _videoAreaSelectionStartPoint is not Point startPoint)
        {
            return false;
        }

        var currentPoint =
            e.GetPosition(VideoAreaSelectionOverlay);

        if (!_isVideoAreaSelecting &&
            Math.Abs(currentPoint.X - startPoint.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - startPoint.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return true;
        }

        _isVideoAreaSelecting = true;

        var selectionBounds = CreateNormalizedRect(
            startPoint,
            currentPoint);

        Canvas.SetLeft(
            VideoAreaSelectionRectangle,
            selectionBounds.Left);

        Canvas.SetTop(
            VideoAreaSelectionRectangle,
            selectionBounds.Top);

        VideoAreaSelectionRectangle.Width =
            selectionBounds.Width;

        VideoAreaSelectionRectangle.Height =
            selectionBounds.Height;

        VideoAreaSelectionRectangle.Visibility =
            Visibility.Visible;

        UpdateVideoAreaSelection(
            sourceList,
            selectionBounds);

        e.Handled = true;
        return true;
    }

    private void UpdateVideoAreaSelection(
        ListBox sourceList,
        Rect selectionBounds)
    {
        var baseline = _videoAreaSelectionBaseline ??
            new HashSet<VideoFileCardViewModel>();

        foreach (var videoFile in sourceList.Items
                     .OfType<VideoFileCardViewModel>())
        {
            var item = sourceList.ItemContainerGenerator
                .ContainerFromItem(videoFile) as ListBoxItem;

            var isInsideSelection = item is not null &&
                GetBoundsRelativeTo(
                    item,
                    VideoAreaSelectionOverlay)
                .IntersectsWith(selectionBounds);

            var shouldBeSelected =
                baseline.Contains(videoFile) ||
                isInsideSelection;

            if (shouldBeSelected &&
                !sourceList.SelectedItems.Contains(videoFile))
            {
                sourceList.SelectedItems.Add(videoFile);
            }
            else if (!shouldBeSelected &&
                     sourceList.SelectedItems.Contains(videoFile))
            {
                sourceList.SelectedItems.Remove(videoFile);
            }
        }
    }

    private async void MainWindow_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_videoAreaSelectionSource is not null)
        {
            EndVideoAreaSelection();
            e.Handled = true;
            return;
        }

        if (_folderDragCandidate is not { } selectedFolder ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        ResetDragCandidates();
        e.Handled = true;

        SetVideoFilesPanelVisible(true);

        await viewModel.VideoFiles.SelectFolderAsync(
            selectedFolder);
    }

    private void EndVideoAreaSelection()
    {
        if (_videoAreaSelectionSource?.IsMouseCaptured == true)
        {
            _videoAreaSelectionSource.ReleaseMouseCapture();
        }

        _videoAreaSelectionSource = null;
        _videoAreaSelectionStartPoint = null;
        _videoAreaSelectionBaseline = null;
        _isVideoAreaSelecting = false;

        if (VideoAreaSelectionRectangle is not null)
        {
            VideoAreaSelectionRectangle.Visibility =
                Visibility.Collapsed;
        }
    }

    private static Rect CreateNormalizedRect(
        Point first,
        Point second) =>
        new(
            new Point(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y)),
            new Point(
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y)));

    private static Rect GetBoundsRelativeTo(
        FrameworkElement element,
        Visual relativeTo) =>
        element.TransformToVisual(relativeTo)
            .TransformBounds(
                new Rect(element.RenderSize));

    private static T? FindVisualAncestor<T>(
        DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source;
             current is not null;
             current = GetVisualOrContentParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? GetVisualOrContentParent(
        DependencyObject source)
    {
        if (source is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement) ??
                (contentElement as FrameworkContentElement)?.Parent;
        }

        return VisualTreeHelper.GetParent(source);
    }

    private bool TryStartFolderDrag(MouseEventArgs e)
    {
        if (_folderDragStartPoint is not Point startPoint ||
            _folderDragCandidate is not { } folder)
        {
            return false;
        }

        var currentPoint = e.GetPosition(this);

        if (Math.Abs(currentPoint.X - startPoint.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - startPoint.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return false;
        }

        ResetDragCandidates();

        if (!folder.IsAvailable ||
            !Directory.Exists(folder.FullPath))
        {
            if (DataContext is MainWindowViewModel unavailableViewModel)
            {
                unavailableViewModel.StatusText =
                    $"Папка недоступна для переноса: {folder.Name}";
            }

            return true;
        }

        var data = new DataObject(
            DataFormats.FileDrop,
            new[] { folder.FullPath });

        e.Handled = true;

        DragDrop.DoDragDrop(
            SearchResultsTree,
            data,
            DragDropEffects.Copy);

        return true;
    }

    private void ResetDragCandidates()
    {
        _videoDragStartPoint = null;
        _videoDragSourceList = null;
        _videoDragCandidate = null;
        _videoDragSelectionAtMouseDown = null;
        _folderDragStartPoint = null;
        _folderDragCandidate = null;
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
            videoItem.DataContext is not
                VideoFileCardViewModel videoFile)
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
            if (VideoFilesPanel.Visibility !=
                Visibility.Visible)
            {
                VideoFilesPanel.Visibility =
                    Visibility.Visible;

                ApplyVideoModeLayout();
            }

            return;
        }

        RememberVideoModeColumnWidths();

        SearchResultsPanel.Visibility =
            Visibility.Visible;

        SearchResultsColumn.Width =
            new GridLength(
                1,
                GridUnitType.Star);

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
