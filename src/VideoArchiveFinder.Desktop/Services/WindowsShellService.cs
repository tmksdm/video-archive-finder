using System.Diagnostics;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsShellService : IWindowsShellService
{
    public void OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }
}
