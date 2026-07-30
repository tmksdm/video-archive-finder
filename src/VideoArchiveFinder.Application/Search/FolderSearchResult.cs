namespace VideoArchiveFinder.Application.Search;

public sealed record FolderSearchResult(
    long Id,
    string FullPath,
    string Name,
    string NormalizedName,
    long? ParentFolderId,
    Guid RootSourceId,
    bool IsAvailable,
    int DirectSubfolderCount,
    int DirectVideoFileCount);
