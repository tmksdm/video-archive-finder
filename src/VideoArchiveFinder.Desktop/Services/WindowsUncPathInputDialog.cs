using VideoArchiveFinder.Desktop.Views;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsUncPathInputDialog : IUncPathInputDialog
{
    public string? ShowDialog()
    {
        var dialog = new UncPathDialog();
        var owner = System.Windows.Application.Current?.MainWindow;

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true
            ? dialog.EnteredPath
            : null;
    }
}
