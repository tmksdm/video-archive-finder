using System.Windows;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsArchiveSourceRemovalConfirmationDialog :
    IArchiveSourceRemovalConfirmationDialog
{
    public bool ConfirmRemoval(
        int sourceCount,
        string? singleSourceDisplayName = null,
        string? singleSourceFullPath = null)
    {
        if (sourceCount <= 0)
        {
            return false;
        }

        var message = sourceCount == 1
            ? CreateSingleSourceMessage(
                singleSourceDisplayName,
                singleSourceFullPath)
            : CreateMultipleSourcesMessage(sourceCount);

        var owner = System.Windows.Application.Current?.MainWindow;

        var result = owner is null
            ? MessageBox.Show(
                message,
                "Удаление источников",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                message,
                "Удаление источников",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    private static string CreateSingleSourceMessage(
        string? displayName,
        string? fullPath)
    {
        return
            $"Удалить источник «{displayName}» из приложения?\n\n" +
            $"{fullPath}\n\n" +
            "Папка и все файлы на диске останутся без изменений.";
    }

    private static string CreateMultipleSourcesMessage(int sourceCount)
    {
        return
            "Удалить выбранные источники из приложения?\n\n" +
            $"Количество выбранных источников: {sourceCount}\n\n" +
            "Папки и все файлы на диске останутся без изменений.";
    }
}
