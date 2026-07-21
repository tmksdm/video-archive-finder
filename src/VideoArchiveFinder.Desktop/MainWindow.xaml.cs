using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
