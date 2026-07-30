namespace VideoArchiveFinder.Application.Search;

public interface IFolderNameHighlightService
{
    IReadOnlyList<FolderNameTextSegment> CreateSegments(
        string folderName,
        string queryText,
        FolderSearchMode searchMode);
}
