using System.Windows;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.Views;

public partial class ArchiveSourceIndexingModeDialog : Window
{
    public ArchiveSourceIndexingModeDialog(string sourcePath)
    {
        InitializeComponent();
        SourcePathText.Text = sourcePath;
    }

    public ArchiveSourceIndexingMode SelectedMode { get; private set; }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = VideoFileNamesOption.IsChecked == true
            ? ArchiveSourceIndexingMode.VideoFileNames
            : BothOption.IsChecked == true
                ? ArchiveSourceIndexingMode.FolderAndVideoFileNames
                : ArchiveSourceIndexingMode.FolderNames;

        DialogResult = true;
    }
}
