using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Application.Thumbnails;
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
        Microsoft.Extensions.Logging.ILogger<MainWindow>
            hoverScrubLogger,
        Microsoft.Extensions.Logging.ILogger<
            CacheSettingsDialog> cacheDialogLogger)
    {
        _appThemeService = appThemeService;
        _thumbnailCacheService = thumbnailCacheService;
        _windowsShellService = windowsShellService;
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
