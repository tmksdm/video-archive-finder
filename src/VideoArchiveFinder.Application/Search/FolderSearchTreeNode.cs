namespace VideoArchiveFinder.Application.Search;

public sealed record FolderSearchTreeNode(
    long Id,
    string FullPath,
    string Name,
    Guid RootSourceId,
    bool IsAvailable,
    bool IsMatch,
    IReadOnlyList<FolderNameTextSegment> NameSegments,
    IReadOnlyList<FolderSearchTreeNode> Children,
    int DirectSubfolderCount = 0,
    int DirectVideoFileCount = 0,
    bool IsVideoFile = false,
    long? NavigationFolderId = null,
    string? NavigationFolderFullPath = null);
