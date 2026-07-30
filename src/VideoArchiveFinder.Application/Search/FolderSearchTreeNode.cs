namespace VideoArchiveFinder.Application.Search;

public sealed record FolderSearchTreeNode(
    long Id,
    string FullPath,
    string Name,
    Guid RootSourceId,
    bool IsAvailable,
    bool IsMatch,
    IReadOnlyList<FolderSearchTreeNode> Children);
