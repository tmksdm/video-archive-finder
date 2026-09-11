using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.Services;

public interface IArchiveSourceIndexingModeDialog
{
    ArchiveSourceIndexingMode? ShowDialog(string sourcePath);
}
