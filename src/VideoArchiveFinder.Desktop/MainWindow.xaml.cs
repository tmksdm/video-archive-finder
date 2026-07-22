using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        sourceItem.IsSelected = true;
        sourceItem.Focus();
    }
}
