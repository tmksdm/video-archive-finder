using VideoArchiveFinder.Application.ArchiveSources;

namespace VideoArchiveFinder.Infrastructure.ArchiveSources;

public sealed class SystemArchivePathProbe : IArchivePathProbe
{
    public bool DirectoryExists(string fullPath)
    {
        return Directory.Exists(fullPath);
    }
}
