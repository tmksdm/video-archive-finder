namespace VideoArchiveFinder.Application.Indexing;

public sealed record IndexedFolder(
    long Id,
    string FullPath,
    string Name,
    string NormalizedName,
    string SearchTokens,
    string SearchStems,
    long? ParentFolderId,
    Guid RootSourceId,
    bool IsAvailable,
    DateTimeOffset LastSeenUtc,
    int DirectSubfolderCount,
    int DirectVideoFileCount);
