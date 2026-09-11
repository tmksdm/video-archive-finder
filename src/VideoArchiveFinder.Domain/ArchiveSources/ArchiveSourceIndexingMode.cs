namespace VideoArchiveFinder.Domain.ArchiveSources;

public enum ArchiveSourceIndexingMode
{
    FolderNames = 0,
    VideoFileNames = 1,
    FolderAndVideoFileNames = 2
}

public static class ArchiveSourceIndexingModeExtensions
{
    public static bool IncludesFolderNames(
        this ArchiveSourceIndexingMode mode)
    {
        return mode is ArchiveSourceIndexingMode.FolderNames
            or ArchiveSourceIndexingMode.FolderAndVideoFileNames;
    }

    public static bool IncludesVideoFileNames(
        this ArchiveSourceIndexingMode mode)
    {
        return mode is ArchiveSourceIndexingMode.VideoFileNames
            or ArchiveSourceIndexingMode.FolderAndVideoFileNames;
    }
}
