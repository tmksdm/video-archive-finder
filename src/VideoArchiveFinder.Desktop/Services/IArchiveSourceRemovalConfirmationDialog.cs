namespace VideoArchiveFinder.Desktop.Services;

public interface IArchiveSourceRemovalConfirmationDialog
{
    bool ConfirmRemoval(
        int sourceCount,
        string? singleSourceDisplayName = null,
        string? singleSourceFullPath = null);
}
