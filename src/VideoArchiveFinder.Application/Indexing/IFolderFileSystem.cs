namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderFileSystem
{
    FileAttributes GetAttributes(
        string directoryPath);

    IReadOnlyList<string> GetDirectories(
        string directoryPath);
}
