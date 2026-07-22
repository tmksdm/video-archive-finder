namespace VideoArchiveFinder.Application.ArchiveSources;

public interface IArchivePathProbe
{
    bool DirectoryExists(string fullPath);
}
