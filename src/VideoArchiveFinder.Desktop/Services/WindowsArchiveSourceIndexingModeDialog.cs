using VideoArchiveFinder.Desktop.Views;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsArchiveSourceIndexingModeDialog
    : IArchiveSourceIndexingModeDialog
{
    public ArchiveSourceIndexingMode? ShowDialog(string sourcePath)
    {
        var dialog = new ArchiveSourceIndexingModeDialog(sourcePath);
        var owner = System.Windows.Application.Current?.MainWindow;

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true
            ? dialog.SelectedMode
            : null;
    }
}
