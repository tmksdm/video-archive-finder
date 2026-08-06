using System.Diagnostics;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsShellService : IWindowsShellService
{
    public void OpenFolder(string folderPath)
    {
        OpenPath(folderPath);
    }

    public void OpenFile(string filePath)
    {
        OpenPath(filePath);
    }

    private static void OpenPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
