namespace VideoArchiveFinder.Desktop.Services;

public interface IWindowsShellService
{
    void OpenFolder(string folderPath);

    void OpenFile(string filePath);
}
