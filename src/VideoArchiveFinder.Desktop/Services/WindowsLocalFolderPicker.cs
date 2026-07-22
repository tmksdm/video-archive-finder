using Microsoft.Win32;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsLocalFolderPicker : ILocalFolderPicker
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку видеоархива",
            Multiselect = false,
            AddToRecent = false
        };

        var owner = System.Windows.Application.Current?.MainWindow;

        var wasAccepted = owner is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(owner);

        return wasAccepted == true
            ? dialog.FolderName
            : null;
    }
}
