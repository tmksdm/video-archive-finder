using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
        var searchVisibility = isVisible
            ? Visibility.Collapsed
            : Visibility.Visible;

        SearchResultsTitle.Visibility = searchVisibility;
        SearchResultsSummary.Visibility = searchVisibility;
        SearchResultsTree.Visibility = searchVisibility;

        VideoFilesPanel.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }






}
