using System.Windows;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsArchiveSourceRemovalConfirmationDialog :
    IArchiveSourceRemovalConfirmationDialog
{
    public bool ConfirmRemoval(
        string displayName,
        string fullPath)
    {
        var message =
            $"Удалить источник «{displayName}» из приложения?\n\n" +
            $"{fullPath}\n\n" +
            "Папка и все файлы на диске останутся без изменений.";

        var owner = System.Windows.Application.Current?.MainWindow;

        var result = owner is null
            ? MessageBox.Show(
                message,
                "Удаление источника",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                message,
                "Удаление источника",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
