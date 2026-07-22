namespace VideoArchiveFinder.Desktop.Services;

public interface IArchiveSourceRemovalConfirmationDialog
{
    bool ConfirmRemoval(
        string displayName,
        string fullPath);
}
